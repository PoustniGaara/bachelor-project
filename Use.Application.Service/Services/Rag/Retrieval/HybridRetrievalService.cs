using Microsoft.Extensions.Options;
using Use.Application.Service.Configuration;
using Use.Application.Service.Models.Retrieval;
using Use.Application.Service.Services.LexicalSearch;
using Use.Application.Service.Services.VectorSearch;

namespace Use.Application.Service.Services.Rag.Retrieval;

/// <summary>
/// Default <see cref="IHybridRetrievalService"/>. Runs the semantic and lexical
/// passes (in parallel when both apply) and merges them with Reciprocal Rank
/// Fusion.
///
/// <para>
/// RRF deliberately fuses by <em>rank</em>, not by raw score, because Qdrant
/// cosine similarities and PostgreSQL ts_rank_cd scores are not comparable.
/// For each method a chunk at 1-based rank <c>r</c> contributes
/// <c>1 / (RrfK + r)</c> to its fused score. A chunk found by both methods
/// accumulates both contributions; a chunk found by one method still gets that
/// method's contribution.
/// </para>
/// </summary>
public sealed class HybridRetrievalService : IHybridRetrievalService
{
    private const int TopFusedToLog = 10;

    private readonly IVectorSearchService _vectorSearch;
    private readonly ILexicalSearchService _lexicalSearch;
    private readonly RagOptions _options;
    private readonly ILogger<HybridRetrievalService> _logger;

    public HybridRetrievalService(
        IVectorSearchService vectorSearch,
        ILexicalSearchService lexicalSearch,
        IOptions<RagOptions> options,
        ILogger<HybridRetrievalService> logger)
    {
        _vectorSearch = vectorSearch;
        _lexicalSearch = lexicalSearch;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FusedChunkResult>> RetrieveAsync(
        string question,
        IReadOnlyList<float>? questionEmbedding,
        RetrievalMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var runSemantic = mode == RetrievalMode.Hybrid && questionEmbedding is { Count: > 0 };
        var runLexical = mode is RetrievalMode.Hybrid or RetrievalMode.LexicalOnly;

        if (mode == RetrievalMode.Hybrid && questionEmbedding is not { Count: > 0 })
            _logger.LogWarning("Hybrid retrieval requested but no question embedding was provided — running lexical-only.");

        if (runLexical && !_lexicalSearch.Enabled)
            _logger.LogWarning("Lexical retrieval requested but PostgreSQL is disabled — skipping the lexical pass.");

        var semanticTask = runSemantic
            ? _vectorSearch.SearchAsync(questionEmbedding!, Math.Max(1, _options.InitialTopK), cancellationToken)
            : Task.FromResult(new VectorSearchResult());

        var lexicalTask = runLexical
            ? _lexicalSearch.SearchAsync(question, Math.Max(1, _options.LexicalTopK), sourceSystem: null, cancellationToken)
            : Task.FromResult((IReadOnlyList<LexicalSearchResult>)Array.Empty<LexicalSearchResult>());

        await Task.WhenAll(semanticTask, lexicalTask).ConfigureAwait(false);

        var semantic = (await semanticTask.ConfigureAwait(false)).Chunks;
        var lexical = await lexicalTask.ConfigureAwait(false);

        var fused = Fuse(semantic, lexical);

        _logger.LogInformation(
            "Hybrid retrieval (mode={Mode}): semantic={Semantic}, lexical={Lexical}, fused={Fused} (RrfK={RrfK}).",
            mode, semantic.Count, lexical.Count, fused.Count, _options.RrfK);

        LogTopFused(fused);

        return fused;
    }

    private IReadOnlyList<FusedChunkResult> Fuse(
        IReadOnlyList<RetrievedChunk> semantic,
        IReadOnlyList<LexicalSearchResult> lexical)
    {
        double k = Math.Max(1, _options.RrfK);
        var byChunkId = new Dictionary<string, FusedChunkResult>(StringComparer.Ordinal);

        // Semantic pass — order is the ranking produced by Qdrant.
        for (var i = 0; i < semantic.Count; i++)
        {
            var chunk = semantic[i];
            if (string.IsNullOrEmpty(chunk.ChunkId)) continue;

            var rank = i + 1;
            var fused = GetOrCreate(byChunkId, chunk.ChunkId, () => FromSemantic(chunk));
            fused.SemanticRank = rank;
            fused.SemanticScore = chunk.Score;
            fused.FusedScore += 1d / (k + rank);
        }

        // Lexical pass — order is the ts_rank_cd ranking from PostgreSQL.
        for (var i = 0; i < lexical.Count; i++)
        {
            var lex = lexical[i];
            if (string.IsNullOrEmpty(lex.ChunkId)) continue;

            var rank = i + 1;
            var fused = GetOrCreate(byChunkId, lex.ChunkId, () => FromLexical(lex));
            fused.LexicalRank = rank;
            fused.LexicalScore = lex.Score;
            fused.FusedScore += 1d / (k + rank);
            EnrichFromLexical(fused, lex);
        }

        return byChunkId.Values
            .OrderByDescending(f => f.FusedScore)
            .ThenBy(f => f.BestRank)
            .ThenBy(f => f.ChunkId, StringComparer.Ordinal)
            .ToList();
    }

    private void LogTopFused(IReadOnlyList<FusedChunkResult> fused)
    {
        if (!_logger.IsEnabled(LogLevel.Debug) || fused.Count == 0) return;

        foreach (var f in fused.Take(TopFusedToLog))
        {
            _logger.LogDebug(
                "Fused chunk {ChunkId}: fusedScore={Fused:F5}, semanticRank={SemRank}, lexicalRank={LexRank} ({System}:{DocId}).",
                f.ChunkId, f.FusedScore,
                f.SemanticRank?.ToString() ?? "-", f.LexicalRank?.ToString() ?? "-",
                f.SourceSystem, f.SourceDocumentId);
        }
    }

    private static FusedChunkResult GetOrCreate(
        IDictionary<string, FusedChunkResult> map, string chunkId, Func<FusedChunkResult> factory)
    {
        if (map.TryGetValue(chunkId, out var existing)) return existing;
        var created = factory();
        map[chunkId] = created;
        return created;
    }

    private static FusedChunkResult FromSemantic(RetrievedChunk chunk) => new()
    {
        ChunkId = chunk.ChunkId,
        SourceSystem = chunk.SourceSystem ?? string.Empty,
        SourceDocumentId = chunk.SourceDocumentId ?? string.Empty,
        ChunkOrder = chunk.ChunkOrder ?? 0,
        Title = chunk.SourceTitle,
        Url = chunk.SourceUrl,
        HeadingPath = chunk.Metadata.TryGetValue("headingPath", out var hp) ? hp : null,
        Text = chunk.Text,
        Metadata = new Dictionary<string, string>(chunk.Metadata, StringComparer.OrdinalIgnoreCase)
    };

    private static FusedChunkResult FromLexical(LexicalSearchResult lex) => new()
    {
        ChunkId = lex.ChunkId,
        SourceSystem = lex.SourceSystem,
        SourceDocumentId = lex.SourceDocumentId,
        ChunkOrder = lex.ChunkOrder,
        Title = lex.Title,
        Url = lex.Url,
        HeadingPath = lex.HeadingPath,
        Text = lex.Text
    };

    // When a chunk was first seen via the semantic pass, the lexical row carries
    // the authoritative joined document metadata — fill any gaps from it.
    private static void EnrichFromLexical(FusedChunkResult fused, LexicalSearchResult lex)
    {
        if (string.IsNullOrEmpty(fused.SourceSystem)) fused.SourceSystem = lex.SourceSystem;
        if (string.IsNullOrEmpty(fused.SourceDocumentId)) fused.SourceDocumentId = lex.SourceDocumentId;
        if (string.IsNullOrWhiteSpace(fused.Title)) fused.Title = lex.Title;
        if (string.IsNullOrWhiteSpace(fused.Url)) fused.Url = lex.Url;
        if (string.IsNullOrWhiteSpace(fused.HeadingPath)) fused.HeadingPath = lex.HeadingPath;
        if (string.IsNullOrEmpty(fused.Text)) fused.Text = lex.Text;
    }
}

