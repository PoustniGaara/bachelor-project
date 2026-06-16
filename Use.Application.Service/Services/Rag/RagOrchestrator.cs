using System.Diagnostics;
using Microsoft.Extensions.Options;
using Use.Application.Service.Common;
using Use.Application.Service.Configuration;
using Use.Application.Service.Evaluation;
using Use.Application.Service.Models.Logging;
using Use.Application.Service.Models.Requests;
using Use.Application.Service.Models.Responses;
using Use.Application.Service.Models.Retrieval;
using Use.Application.Service.Services.Embeddings;
using Use.Application.Service.Services.Prompting;
using Use.Application.Service.Services.Rag.ContextAssembly;
using Use.Application.Service.Services.Rag.DocumentSelection;
using Use.Application.Service.Services.Rag.Retrieval;
using Use.Application.Service.Services.VectorSearch;

namespace Use.Application.Service.Services.Rag;

public sealed class RagOrchestrator : IRagOrchestrator, IRetrievalProbe
{
    private const string NoContextAnswer =
        "I could not find any relevant documentation for that question.";

    /// <summary>How many top retrieval candidates to surface for logging (rag_retrieved_chunk_log).</summary>
    private const int MaxLoggedCandidates = 30;

    private static readonly IReadOnlySet<string> EmptySelected =
        new HashSet<string>(StringComparer.Ordinal);

    private readonly ILlmServiceClient _llm;
    private readonly IVectorSearchService _vectorSearch;
    private readonly IDocumentSelector _documentSelector;
    private readonly IHybridRetrievalService _hybridRetrieval;
    private readonly IHybridDocumentSelector _hybridDocumentSelector;
    private readonly IContextAssembler _contextAssembler;
    private readonly IPromptBuilder _promptBuilder;
    private readonly RagOptions _ragOptions;
    private readonly PostgresOptions _postgresOptions;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<RagOrchestrator> _logger;

    public RagOrchestrator(
        ILlmServiceClient llm,
        IVectorSearchService vectorSearch,
        IDocumentSelector documentSelector,
        IHybridRetrievalService hybridRetrieval,
        IHybridDocumentSelector hybridDocumentSelector,
        IContextAssembler contextAssembler,
        IPromptBuilder promptBuilder,
        IOptions<RagOptions> ragOptions,
        IOptions<PostgresOptions> postgresOptions,
        IHostEnvironment environment,
        ILogger<RagOrchestrator> logger)
    {
        _llm = llm;
        _vectorSearch = vectorSearch;
        _documentSelector = documentSelector;
        _hybridRetrieval = hybridRetrieval;
        _hybridDocumentSelector = hybridDocumentSelector;
        _contextAssembler = contextAssembler;
        _promptBuilder = promptBuilder;
        _ragOptions = ragOptions.Value;
        _postgresOptions = postgresOptions.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<ChatResponse> AnswerAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await ExecuteAsync(request.Question, cancellationToken).ConfigureAwait(false);
        return result.Response;
    }

    public async Task<RagExecutionResult> ExecuteAsync(string question, CancellationToken cancellationToken)
    {
        question = question.Trim();
        if (string.IsNullOrEmpty(question))
            throw new ArgumentException("Question must not be empty.", nameof(question));

        var mode = ResolveMode();
        _logger.LogInformation("RAG pipeline start (retrievalMode={Mode}, questionLength={Length}).",
            mode, question.Length);

        return mode == RetrievalMode.SemanticOnly
            ? await ExecuteSemanticOnlyAsync(question, cancellationToken).ConfigureAwait(false)
            : await ExecuteWithFusionAsync(question, mode, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the effective retrieval mode, applying the Postgres dependency
    /// policy: degrade to SemanticOnly in Development, fail clearly otherwise.
    /// </summary>
    private RetrievalMode ResolveMode()
    {
        var mode = _ragOptions.RetrievalMode;
        var needsPostgres = mode is RetrievalMode.Hybrid or RetrievalMode.LexicalOnly;

        if (needsPostgres && !_postgresOptions.Enabled)
        {
            if (_environment.IsDevelopment())
            {
                _logger.LogWarning(
                    "RetrievalMode={Mode} requires PostgreSQL but Postgres:Enabled=false. " +
                    "Degrading to SemanticOnly (Development).", mode);
                return RetrievalMode.SemanticOnly;
            }

            throw new InvalidOperationException(
                $"RetrievalMode={mode} requires PostgreSQL but Postgres:Enabled=false. " +
                "Enable Postgres or set Rag:RetrievalMode=SemanticOnly.");
        }

        return mode;
    }

    // -----------------------------------------------------------------------
    // Legacy semantic-only path (Qdrant vector search only). Behaviour is
    // intentionally identical to the pre-hybrid pipeline; only telemetry is added.
    // -----------------------------------------------------------------------
    private async Task<RagExecutionResult> ExecuteSemanticOnlyAsync(string question, CancellationToken cancellationToken)
    {
        var retrievalSw = Stopwatch.StartNew();

        // 1) Embed the question.
        _logger.LogDebug("Embedding user question (length={Length}).", question.Length);
        var embedding = await EmbedAsync(question, cancellationToken).ConfigureAwait(false);

        // 2) Initial similarity pass (e.g. top 40).
        var initialTopK = Math.Max(1, _ragOptions.InitialTopK);
        var initial = await _vectorSearch
            .SearchAsync(embedding, initialTopK, cancellationToken)
            .ConfigureAwait(false);
        retrievalSw.Stop();
        var retrievalMs = (int)retrievalSw.ElapsedMilliseconds;

        _logger.LogInformation(
            "Initial similarity pass: {Count}/{Requested} chunks from '{Collection}'.",
            initial.Chunks.Count, initialTopK, initial.CollectionName);

        // Candidates for logging = the raw similarity hits before document selection.
        var candidates = BuildCandidatesFromChunks(initial.Chunks);

        if (initial.Chunks.Count == 0)
            return BuildEmptyResult(RetrievalMode.SemanticOnly, totalRetrieved: 0, retrievalMs, candidates);

        // 3) Pick the top N documents by chunk-hit frequency.
        var topDocs = Math.Max(1, _ragOptions.TopDocuments);
        var selections = _documentSelector.Select(initial.Chunks, topDocs);

        GenerationOutcome outcome;
        if (selections.Count == 0)
        {
            _logger.LogWarning("No usable sourceDocumentId on initial hits — falling back to raw chunks.");
            outcome = await RespondFromChunksAsync(question, initial.Chunks, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            outcome = await AssembleSelectGenerateAsync(question, selections, cancellationToken).ConfigureAwait(false);
        }

        return BuildResult(RetrievalMode.SemanticOnly, initial.Chunks.Count, retrievalMs, candidates, outcome);
    }

    // -----------------------------------------------------------------------
    // Hybrid / lexical-only path: retrieve + RRF fuse, then select documents
    // from the fused chunks. Document expansion + generation are unchanged.
    // -----------------------------------------------------------------------
    private async Task<RagExecutionResult> ExecuteWithFusionAsync(
        string question, RetrievalMode mode, CancellationToken cancellationToken)
    {
        var retrievalSw = Stopwatch.StartNew();

        // Embed only when the semantic pass will run (Hybrid). LexicalOnly skips it.
        IReadOnlyList<float>? embedding = null;
        if (mode == RetrievalMode.Hybrid)
        {
            _logger.LogDebug("Embedding user question (length={Length}).", question.Length);
            embedding = await EmbedAsync(question, cancellationToken).ConfigureAwait(false);
        }

        // 1) Retrieve + fuse (semantic + lexical) by Reciprocal Rank Fusion.
        var fused = await _hybridRetrieval
            .RetrieveAsync(question, embedding, mode, cancellationToken)
            .ConfigureAwait(false);

        if (fused.Count == 0)
        {
            retrievalSw.Stop();
            return BuildEmptyResult(mode, totalRetrieved: 0, (int)retrievalSw.ElapsedMilliseconds,
                Array.Empty<RetrievalCandidate>());
        }

        // 1b) Rerank the top fused candidates (BAAI/bge-reranker-v2-m3 via the
        // LLM service) before document selection. No-op when disabled.
        var reranked = await RerankFusedAsync(question, fused, cancellationToken).ConfigureAwait(false);
        retrievalSw.Stop();
        var retrievalMs = (int)retrievalSw.ElapsedMilliseconds;

        // Candidates for logging = top fused/reranked chunks BEFORE document
        // expansion (the most useful signal for debugging bad answers).
        var candidates = BuildCandidatesFromFused(reranked);

        // 2) Select the top N documents from the (re)ranked fused chunk results.
        var topDocs = Math.Max(1, _ragOptions.TopDocuments);
        var selections = _hybridDocumentSelector.Select(reranked, topDocs);

        GenerationOutcome outcome;
        if (selections.Count == 0)
        {
            _logger.LogWarning("No usable sourceDocumentId on fused hits — falling back to raw fused chunks.");
            var rawChunks = reranked.Select(f => f.ToRetrievedChunk()).ToList();
            outcome = await RespondFromChunksAsync(question, rawChunks, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            outcome = await AssembleSelectGenerateAsync(question, selections, cancellationToken).ConfigureAwait(false);
        }

        return BuildResult(mode, fused.Count, retrievalMs, candidates, outcome);
    }

    /// <summary>
    /// Reranks the top <c>Rag:RerankTopK</c> fused candidates with the dedicated
    /// reranker (via the LLM service), reordering them by relevance score before
    /// document selection. The remaining (lower) fused candidates keep their
    /// fused order and are appended after the reranked block.
    ///
    /// <para>
    /// When <c>Rag:RerankingEnabled=false</c> the input list is returned
    /// unchanged, preserving the legacy fused-score behaviour. A reranker failure
    /// surfaces as an <see cref="InvalidOperationException"/> (→ 502), consistent
    /// with the other LLM service calls.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<FusedChunkResult>> RerankFusedAsync(
        string question,
        IReadOnlyList<FusedChunkResult> fused,
        CancellationToken cancellationToken)
    {
        if (!_ragOptions.RerankingEnabled)
        {
            _logger.LogInformation(
                "Reranking disabled (Rag:RerankingEnabled=false) — keeping {Count} fused candidates as-is.",
                fused.Count);
            return fused;
        }

        // Fused chunks are already ordered by fused score descending.
        var topK = Math.Max(1, _ragOptions.RerankTopK);
        var candidates = fused
            .Where(c => !string.IsNullOrWhiteSpace(c.ChunkId) && !string.IsNullOrWhiteSpace(c.Text))
            .Take(topK)
            .ToList();

        _logger.LogInformation(
            "Reranking enabled (RerankTopK={TopK}): {Candidates} of {Fused} fused candidates sent to the reranker.",
            topK, candidates.Count, fused.Count);

        if (candidates.Count == 0)
            return fused;

        var documents = candidates
            .Select(c => new RerankDocument { ChunkId = c.ChunkId, Text = c.Text })
            .ToList();

        var response = await _llm.RerankAsync(question, documents, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Reranker returned {Count} scores (model={Model}).",
            response.Results.Count, response.Model);

        // Map scores back onto the fused candidates by chunkId.
        var scoreByChunkId = response.Results
            .GroupBy(r => r.ChunkId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Score, StringComparer.Ordinal);

        var candidateIds = new HashSet<string>(candidates.Select(c => c.ChunkId), StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            if (scoreByChunkId.TryGetValue(candidate.ChunkId, out var score))
            {
                candidate.RerankScore = score;
                candidate.WasReranked = true;
            }
        }

        // Reranked block: scored candidates ordered by rerank score desc
        // (fused score as a tie-break), followed by any candidate the reranker
        // did not score (keeps fused order). Assign 1-based reranked ranks.
        var rerankedBlock = candidates
            .OrderByDescending(c => c.WasReranked)
            .ThenByDescending(c => c.RerankScore ?? double.MinValue)
            .ThenByDescending(c => c.FusedScore)
            .ToList();

        for (var i = 0; i < rerankedBlock.Count; i++)
            rerankedBlock[i].RerankedRank = i + 1;

        // Tail: fused candidates that were not sent to the reranker, in fused order.
        var tail = fused.Where(c => !candidateIds.Contains(c.ChunkId)).ToList();

        LogTopReranked(rerankedBlock);

        return rerankedBlock.Concat(tail).ToList();
    }

    private void LogTopReranked(IReadOnlyList<FusedChunkResult> reranked)
    {
        if (!_logger.IsEnabled(LogLevel.Debug)) return;

        foreach (var c in reranked.Take(10))
        {
            _logger.LogDebug(
                "Reranked chunk {ChunkId}: rerankScore={Rerank}, rerankedRank={RrRank}, fusedScore={Fused:F5}, " +
                "semanticRank={SemRank}, lexicalRank={LexRank} ({System}:{DocId}).",
                c.ChunkId,
                c.RerankScore?.ToString("F5") ?? "-",
                c.RerankedRank?.ToString() ?? "-",
                c.FusedScore,
                c.SemanticRank?.ToString() ?? "-",
                c.LexicalRank?.ToString() ?? "-",
                c.SourceSystem, c.SourceDocumentId);
        }
    }

    /// <summary>
    /// Shared tail of both pipelines: expand each selected document to its full
    /// ordered chunk list, build the prompt, generate, and assemble the response.
    /// Returns the response plus the chunk ids actually fed to the LLM (for the
    /// retrieved-chunk log) and the generation duration.
    /// </summary>
    private async Task<GenerationOutcome> AssembleSelectGenerateAsync(
        string question,
        IReadOnlyList<DocumentSelection.DocumentSelection> selections,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Selected top {N} documents: {Docs}",
            selections.Count,
            string.Join(", ", selections.Select(s =>
                $"{s.SourceSystem}:{s.SourceDocumentId}(hits={s.HitCount}, best={s.BestScore:F4}, docScore={s.DocumentScore:F4})")));

        // Expand each selected document to its full ordered chunk list (Qdrant scroll).
        var documents = await _contextAssembler.AssembleAsync(selections, cancellationToken)
            .ConfigureAwait(false);

        // Flatten ordered chunks (document-by-document, in selector priority) for the prompt + response.
        var orderedChunks = documents.SelectMany(d => d.OrderedChunks).ToList();
        if (orderedChunks.Count == 0)
            return new GenerationOutcome(EmptyAnswer(), GenerationDurationMs: null, EmptySelected, HasContext: false);

        // Build prompt + call LLM.
        var generationSw = Stopwatch.StartNew();
        var generation = await GenerateAsync(question, orderedChunks, cancellationToken).ConfigureAwait(false);
        generationSw.Stop();

        // Sources: one entry per top document. Score = document/fused score where
        // available (hybrid), otherwise the best similarity score (semantic-only).
        var sources = documents
            .Select(d => new SourceReference
            {
                Title        = d.Title,
                Url          = d.Url,
                SourceSystem = d.SourceSystem,
                ChunkId      = d.RepresentativeChunkId
                               ?? d.OrderedChunks.FirstOrDefault()?.ChunkId
                               ?? string.Empty,
                Score        = d.DocumentScore > 0d ? (float)d.DocumentScore : d.BestScore
            })
            .ToList();

        var response = new ChatResponse
        {
            Answer = generation.Answer,
            Sources = sources,
            RetrievedChunks = orderedChunks
        };

        return new GenerationOutcome(
            response, (int)generationSw.ElapsedMilliseconds, SelectedIds(orderedChunks), HasContext: true);
    }

    private async Task<GenerationOutcome> RespondFromChunksAsync(
        string question,
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken ct)
    {
        var generationSw = Stopwatch.StartNew();
        var generation = await GenerateAsync(question, chunks, ct).ConfigureAwait(false);
        generationSw.Stop();

        var response = new ChatResponse
        {
            Answer = generation.Answer,
            Sources = chunks.Select(c => new SourceReference
            {
                Title = c.SourceTitle, Url = c.SourceUrl,
                SourceSystem = c.SourceSystem, ChunkId = c.ChunkId, Score = c.Score
            }).ToList(),
            RetrievedChunks = chunks
        };

        return new GenerationOutcome(
            response, (int)generationSw.ElapsedMilliseconds, SelectedIds(chunks), HasContext: true);
    }

    private async Task<IReadOnlyList<float>> EmbedAsync(string question, CancellationToken ct)
    {
        var embedding = await _llm.CreateEmbeddingAsync(
            new EmbeddingRequest { Input = question, SourceType = "UserQuery" }, ct)
            .ConfigureAwait(false);
        return embedding.Embedding;
    }

    private Task<GenerationResponse> GenerateAsync(
        string question, IReadOnlyList<RetrievedChunk> chunks, CancellationToken ct)
    {
        var systemPrompt = _promptBuilder.BuildSystemPrompt();
        var userPrompt   = _promptBuilder.BuildUserPrompt(question, chunks);

        return _llm.GenerateAsync(
            new GenerationRequest { Prompt = userPrompt, SystemPrompt = systemPrompt }, ct);
    }

    private static ChatResponse EmptyAnswer() => new()
    {
        Answer = NoContextAnswer,
        Sources = Array.Empty<SourceReference>(),
        RetrievedChunks = Array.Empty<RetrievedChunk>()
    };

    // -----------------------------------------------------------------------
    // Retrieval-only probe (IRetrievalProbe) — used by the evaluation harness.
    // Reuses the exact same retrieval / fusion / rerank / selection / assembly
    // services as the normal pipeline, but stops BEFORE prompt building and LLM
    // generation. Production chat behaviour is untouched.
    // -----------------------------------------------------------------------
    public async Task<RetrievalProbeResult> ProbeRetrievalAsync(string question, CancellationToken cancellationToken)
    {
        question = question.Trim();
        if (string.IsNullOrEmpty(question))
            throw new ArgumentException("Question must not be empty.", nameof(question));

        var mode = ResolveMode();
        _logger.LogDebug("Retrieval probe start (retrievalMode={Mode}, questionLength={Length}).",
            mode, question.Length);

        return mode == RetrievalMode.SemanticOnly
            ? await ProbeSemanticOnlyAsync(question, cancellationToken).ConfigureAwait(false)
            : await ProbeWithFusionAsync(question, mode, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RetrievalProbeResult> ProbeSemanticOnlyAsync(string question, CancellationToken ct)
    {
        var embedding = await EmbedAsync(question, ct).ConfigureAwait(false);

        var initialTopK = Math.Max(1, _ragOptions.InitialTopK);
        var initial = await _vectorSearch.SearchAsync(embedding, initialTopK, ct).ConfigureAwait(false);

        var semantic = ToProbeCandidatesFromChunks(initial.Chunks);

        var selections = _documentSelector.Select(initial.Chunks, Math.Max(1, _ragOptions.TopDocuments));
        var finalChunks = await AssembleContextChunksAsync(selections, ct).ConfigureAwait(false);

        return new RetrievalProbeResult
        {
            Mode = RetrievalMode.SemanticOnly,
            RerankingApplied = false,
            SemanticCandidates = semantic,
            LexicalCandidates = Array.Empty<ProbeCandidate>(),
            FusedCandidates = semantic,            // no fusion on the semantic-only path
            RerankedCandidates = semantic,         // no reranking on the semantic-only path
            SelectedDocuments = ToProbeDocuments(selections),
            FinalContextChunks = ToProbeCandidatesFromChunks(finalChunks)
        };
    }

    private async Task<RetrievalProbeResult> ProbeWithFusionAsync(
        string question, RetrievalMode mode, CancellationToken ct)
    {
        IReadOnlyList<float>? embedding = null;
        if (mode == RetrievalMode.Hybrid)
            embedding = await EmbedAsync(question, ct).ConfigureAwait(false);

        var fused = await _hybridRetrieval.RetrieveAsync(question, embedding, mode, ct).ConfigureAwait(false);

        // The fused list carries each chunk's original semantic/lexical rank, so
        // the per-pass candidate views are derived without re-running retrieval.
        var semantic = fused
            .Where(f => f.SemanticRank.HasValue && !string.IsNullOrEmpty(f.ChunkId))
            .OrderBy(f => f.SemanticRank!.Value)
            .Select(f => new ProbeCandidate(f.ChunkId, f.SourceSystem, f.SourceDocumentId, f.SemanticRank!.Value))
            .ToList();

        var lexical = fused
            .Where(f => f.LexicalRank.HasValue && !string.IsNullOrEmpty(f.ChunkId))
            .OrderBy(f => f.LexicalRank!.Value)
            .Select(f => new ProbeCandidate(f.ChunkId, f.SourceSystem, f.SourceDocumentId, f.LexicalRank!.Value))
            .ToList();

        var fusedCandidates = ToProbeCandidatesFromFused(fused);

        // Identical reranking step as the normal pipeline (no-op when disabled).
        var reranked = await RerankFusedAsync(question, fused, ct).ConfigureAwait(false);
        var rerankedCandidates = ToProbeCandidatesFromFused(reranked);

        var selections = _hybridDocumentSelector.Select(reranked, Math.Max(1, _ragOptions.TopDocuments));
        var finalChunks = await AssembleContextChunksAsync(selections, ct).ConfigureAwait(false);

        return new RetrievalProbeResult
        {
            Mode = mode,
            RerankingApplied = _ragOptions.RerankingEnabled,
            SemanticCandidates = semantic,
            LexicalCandidates = lexical,
            FusedCandidates = fusedCandidates,
            RerankedCandidates = rerankedCandidates,
            SelectedDocuments = ToProbeDocuments(selections),
            FinalContextChunks = ToProbeCandidatesFromChunks(finalChunks)
        };
    }

    private async Task<IReadOnlyList<RetrievedChunk>> AssembleContextChunksAsync(
        IReadOnlyList<DocumentSelection.DocumentSelection> selections, CancellationToken ct)
    {
        if (selections.Count == 0) return Array.Empty<RetrievedChunk>();
        var documents = await _contextAssembler.AssembleAsync(selections, ct).ConfigureAwait(false);
        return documents.SelectMany(d => d.OrderedChunks).ToList();
    }

    private static IReadOnlyList<ProbeCandidate> ToProbeCandidatesFromChunks(IReadOnlyList<RetrievedChunk> chunks)
    {
        var list = new List<ProbeCandidate>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            if (string.IsNullOrEmpty(c.ChunkId)) continue;
            list.Add(new ProbeCandidate(
                c.ChunkId, c.SourceSystem ?? string.Empty, c.SourceDocumentId ?? string.Empty, i + 1));
        }
        return list;
    }

    private static IReadOnlyList<ProbeCandidate> ToProbeCandidatesFromFused(IReadOnlyList<FusedChunkResult> fused)
    {
        var list = new List<ProbeCandidate>(fused.Count);
        for (var i = 0; i < fused.Count; i++)
        {
            var f = fused[i];
            if (string.IsNullOrEmpty(f.ChunkId)) continue;
            list.Add(new ProbeCandidate(f.ChunkId, f.SourceSystem, f.SourceDocumentId, i + 1));
        }
        return list;
    }

    private static IReadOnlyList<ProbeDocument> ToProbeDocuments(
        IReadOnlyList<DocumentSelection.DocumentSelection> selections)
        => selections
            .Select(s => new ProbeDocument(s.SourceSystem, s.SourceDocumentId, s.Title, s.Url))
            .ToList();

    // -----------------------------------------------------------------------
    // Telemetry helpers
    // -----------------------------------------------------------------------

    private RagExecutionResult BuildResult(
        RetrievalMode mode,
        int totalRetrieved,
        int retrievalMs,
        IReadOnlyList<RetrievalCandidate> candidates,
        GenerationOutcome outcome)
    {
        MarkSelected(candidates, outcome.SelectedChunkIds);

        return new RagExecutionResult
        {
            Response = outcome.Response,
            Mode = mode,
            RetrievalStrategy = RetrievalStrategyNames.From(mode),
            AugmentedQuery = null, // no query rewriting yet
            TotalRetrievedChunks = totalRetrieved,
            SelectedContextChunks = outcome.Response.RetrievedChunks.Count,
            AnswerStatus = outcome.HasContext ? RagAnswerStatus.Completed : RagAnswerStatus.NoRelevantContext,
            RetrievalDurationMs = retrievalMs,
            GenerationDurationMs = outcome.GenerationDurationMs,
            Candidates = candidates
        };
    }

    private RagExecutionResult BuildEmptyResult(
        RetrievalMode mode, int totalRetrieved, int retrievalMs, IReadOnlyList<RetrievalCandidate> candidates)
        => new()
        {
            Response = EmptyAnswer(),
            Mode = mode,
            RetrievalStrategy = RetrievalStrategyNames.From(mode),
            AugmentedQuery = null,
            TotalRetrievedChunks = totalRetrieved,
            SelectedContextChunks = 0,
            AnswerStatus = RagAnswerStatus.NoRelevantContext,
            RetrievalDurationMs = retrievalMs,
            GenerationDurationMs = null,
            Candidates = candidates
        };

    private static IReadOnlyList<RetrievalCandidate> BuildCandidatesFromChunks(IReadOnlyList<RetrievedChunk> chunks)
    {
        var list = new List<RetrievalCandidate>(Math.Min(chunks.Count, MaxLoggedCandidates));
        for (var i = 0; i < chunks.Count && list.Count < MaxLoggedCandidates; i++)
        {
            var c = chunks[i];
            if (string.IsNullOrEmpty(c.ChunkId)) continue;

            list.Add(new RetrievalCandidate
            {
                ChunkId = c.ChunkId,
                SourceSystem = c.SourceSystem ?? string.Empty,
                SourceDocumentId = c.SourceDocumentId ?? string.Empty,
                Rank = i + 1,
                SemanticScore = c.Score,
                LexicalScore = null,
                RrfScore = null
            });
        }

        return list;
    }

    private static IReadOnlyList<RetrievalCandidate> BuildCandidatesFromFused(IReadOnlyList<FusedChunkResult> fused)
    {
        var list = new List<RetrievalCandidate>(Math.Min(fused.Count, MaxLoggedCandidates));
        for (var i = 0; i < fused.Count && list.Count < MaxLoggedCandidates; i++)
        {
            var f = fused[i];
            if (string.IsNullOrEmpty(f.ChunkId)) continue;

            list.Add(new RetrievalCandidate
            {
                ChunkId = f.ChunkId,
                SourceSystem = f.SourceSystem,
                SourceDocumentId = f.SourceDocumentId,
                Rank = i + 1,
                SemanticScore = f.SemanticScore,
                LexicalScore = f.LexicalScore,
                RrfScore = f.FusedScore
            });
        }

        return list;
    }

    private static void MarkSelected(IReadOnlyList<RetrievalCandidate> candidates, IReadOnlySet<string> selectedChunkIds)
    {
        if (selectedChunkIds.Count == 0) return;

        foreach (var candidate in candidates)
            if (selectedChunkIds.Contains(candidate.ChunkId))
                candidate.WasSelectedForContext = true;
    }

    private static IReadOnlySet<string> SelectedIds(IReadOnlyList<RetrievedChunk> chunks)
        => chunks
            .Where(c => !string.IsNullOrEmpty(c.ChunkId))
            .Select(c => c.ChunkId)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Internal carrier from the generation tail back to the result builder.</summary>
    private sealed record GenerationOutcome(
        ChatResponse Response,
        int? GenerationDurationMs,
        IReadOnlySet<string> SelectedChunkIds,
        bool HasContext);
}