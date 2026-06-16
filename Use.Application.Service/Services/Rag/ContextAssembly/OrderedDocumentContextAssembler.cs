using Microsoft.Extensions.Options;
using Use.Application.Service.Configuration;
using Use.Application.Service.Models.Retrieval;
using Use.Application.Service.Services.VectorSearch;

namespace Use.Application.Service.Services.Rag.ContextAssembly;

public sealed class OrderedDocumentContextAssembler : IContextAssembler
{
    private readonly IVectorSearchService _vectorSearch;
    private readonly RagOptions _options;
    private readonly ILogger<OrderedDocumentContextAssembler> _logger;

    public OrderedDocumentContextAssembler(
        IVectorSearchService vectorSearch,
        IOptions<RagOptions> options,
        ILogger<OrderedDocumentContextAssembler> logger)
    {
        _vectorSearch = vectorSearch;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DocumentContext>> AssembleAsync(
        IReadOnlyList<DocumentSelection.DocumentSelection> selections,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (selections.Count == 0) return Array.Empty<DocumentContext>();

        // Pull each document's chunks in parallel — they're independent calls.
        var tasks = selections.Select(async sel =>
        {
            var chunks = await _vectorSearch
                .ListByDocumentAsync(sel.SourceSystem, sel.SourceDocumentId,
                    _options.MaxChunksPerDocument, cancellationToken)
                .ConfigureAwait(false);

            var ordered = chunks
                .OrderBy(c => c.ChunkOrder ?? int.MaxValue)
                .ThenBy(c => c.ChunkId, StringComparer.Ordinal)
                .ToList();

            _logger.LogDebug(
                "Assembled {Count} ordered chunks for document {System}:{DocId}.",
                ordered.Count, sel.SourceSystem, sel.SourceDocumentId);

            return new DocumentContext
            {
                SourceSystem     = sel.SourceSystem,
                SourceDocumentId = sel.SourceDocumentId,
                Title            = sel.Title ?? ordered.FirstOrDefault()?.SourceTitle,
                Url              = sel.Url   ?? ordered.FirstOrDefault()?.SourceUrl,
                HitCount         = sel.HitCount,
                BestScore        = sel.BestScore,
                DocumentScore    = sel.DocumentScore,
                RepresentativeChunkId = sel.RepresentativeChunkId,
                OrderedChunks    = ordered
            };
        });

        var contexts = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Keep the same priority ordering the selector produced.
        return contexts;
    }
}