using System.Diagnostics;
using Microsoft.Extensions.Options;
using Use.Indexing.Worker.Chunking;
using Use.Indexing.Worker.Configuration;
using Use.Indexing.Worker.Connectors;
using Use.Indexing.Worker.Diagnostics;
using Use.Indexing.Worker.Embeddings;using Use.Indexing.Worker.Models;
using Use.Indexing.Worker.Normalization;
using Use.Indexing.Worker.Parsing;
using Use.Indexing.Worker.Persistence;
using Use.Indexing.Worker.Persistence.Postgres;

namespace Use.Indexing.Worker.Orchestration;

/// <summary>
/// Default orchestrator: walks all registered connectors, runs the per-document
/// pipeline, isolates failures so one bad document does not abort the cycle,
/// and reports an aggregated <see cref="IndexingResult"/>.
/// </summary>
public sealed class IndexingOrchestrator : IIndexingOrchestrator
{
    private readonly IEnumerable<ISourceConnector> _connectors;
    private readonly IDocumentParser _parser;
    private readonly ITextNormalizer _normalizer;
    private readonly IChunkingService _chunker;
    private readonly IEmbeddingTextBuilder _embeddingTextBuilder;
    private readonly IEmbeddingService _embedder;
    private readonly IIndexRepository _repository;
    private readonly IVectorStore _vectorStore;
    private readonly ISqlChunkRepository _sqlChunkStore;
    private readonly IChunkDumpWriter _chunkDump;
    private readonly IndexingOptions _options;
    private readonly ILogger<IndexingOrchestrator> _logger;

    public IndexingOrchestrator(
        IEnumerable<ISourceConnector> connectors,
        IDocumentParser parser,
        ITextNormalizer normalizer,
        IChunkingService chunker,
        IEmbeddingTextBuilder embeddingTextBuilder,
        IEmbeddingService embedder,
        IIndexRepository repository,
        IVectorStore vectorStore,
        ISqlChunkRepository sqlChunkStore,
        IChunkDumpWriter chunkDump,
        IOptions<IndexingOptions> options,
        ILogger<IndexingOrchestrator> logger)
    {
        _connectors = connectors;
        _parser = parser;
        _normalizer = normalizer;
        _chunker = chunker;
        _embeddingTextBuilder = embeddingTextBuilder;
        _embedder = embedder;
        _repository = repository;
        _vectorStore = vectorStore;
        _sqlChunkStore = sqlChunkStore;
        _chunkDump = chunkDump;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IndexingResult> RunAsync(IndexingJobOptions options, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var maxDocs = options.MaxDocuments ?? _options.MaxDocumentsPerCycle;
        var fullReindex = options.FullReindex || _options.ForceFullReindex;

        int discovered = 0, indexed = 0, failed = 0, chunksWritten = 0;

        var connectors = options.OnlySource is { } only
            ? _connectors.Where(c => c.SourceSystem == only)
            : _connectors;

        foreach (var connector in connectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _chunkDump.BeginCycle(connector.SourceSystem);

            // Incremental indexing: ask repo for last-indexed cursor unless full-reindex.
            DateTimeOffset? since = null;
            if (!fullReindex)
            {
                since = await _repository.GetLastIndexedAtAsync(connector.SourceSystem, cancellationToken);
            }

            _logger.LogInformation(
                "Indexing source {Source} (full={Full}, since={Since})",
                connector.Name, fullReindex, since);

            var cycleStart = DateTimeOffset.UtcNow;

            await foreach (var reference in connector.DiscoverAsync(since, cancellationToken))
            {
                if (discovered >= maxDocs)
                {
                    _logger.LogWarning("Reached MaxDocumentsPerCycle={Max}; stopping discovery.", maxDocs);
                    break;
                }

                if (options.OnlySourceDocumentId is { } onlyId &&
                    !string.Equals(onlyId, reference.SourceDocumentId, StringComparison.Ordinal))
                {
                    continue;
                }

                discovered++;

                try
                {
                    var written = await ProcessDocumentAsync(connector, reference, cancellationToken);
                    chunksWritten += written;
                    indexed++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    // Do not include document content in the log — only ids/url.
                    _logger.LogError(ex,
                        "Failed to index document {Source}:{DocId} ({Url})",
                        reference.SourceSystem, reference.SourceDocumentId, reference.Url);
                }
            }

            await _repository.SetLastIndexedAtAsync(connector.SourceSystem, cycleStart, cancellationToken);
        }

        sw.Stop();
        var result = new IndexingResult(discovered, indexed, failed, chunksWritten, sw.Elapsed);
        _logger.LogInformation(
            "Indexing cycle complete: discovered={Discovered} indexed={Indexed} failed={Failed} chunks={Chunks} duration={Duration}",
            result.DocumentsDiscovered, result.DocumentsIndexed, result.DocumentsFailed,
            result.ChunksWritten, result.Duration);
        return result;
    }

    private async Task<int> ProcessDocumentAsync(
        ISourceConnector connector,
        SourceDocumentReference reference,
        CancellationToken cancellationToken)
    {
        var source = await connector.FetchAsync(reference, cancellationToken);
        var parsed = await _parser.ParseAsync(source, cancellationToken);
        var normalized = _normalizer.Normalize(parsed);

        await _repository.UpsertDocumentAsync(normalized, cancellationToken);

        var chunks = _chunker.Chunk(normalized);

        // Build the enriched text used as embedding input. The chunks' clean
        // Text is preserved untouched and remains the canonical RAG context.
        chunks = _embeddingTextBuilder.Enrich(chunks);

        await _chunkDump.WriteAsync(normalized, chunks, cancellationToken);

        if (chunks.Count == 0)
        {
            _logger.LogDebug("No chunks for {Source}:{DocId}", reference.SourceSystem, reference.SourceDocumentId);
            // Ensure stale vectors are removed if the document became empty.
            await _vectorStore.DeleteByDocumentAsync(reference, cancellationToken);
            await _repository.ReplaceChunksAsync(reference, chunks, cancellationToken);
            // Mirror replace semantics in the lexical store: keep the document
            // row but clear its (now empty) chunks.
            await _sqlChunkStore.ReplaceDocumentAsync(normalized, chunks, cancellationToken);
            return 0;
        }

        var embedded = await _embedder.EmbedAsync(chunks, cancellationToken);

        // Replace strategy keeps the index consistent with the latest source state.
        // Qdrant (vectors) and PostgreSQL (lexical/BM25) are updated independently
        // but share the same deterministic chunk ids.
        await _vectorStore.DeleteByDocumentAsync(reference, cancellationToken);
        await _repository.ReplaceChunksAsync(reference, chunks, cancellationToken);
        await _vectorStore.UpsertAsync(embedded, cancellationToken);
        await _sqlChunkStore.ReplaceDocumentAsync(normalized, chunks, cancellationToken);

        return chunks.Count;
    }
}

