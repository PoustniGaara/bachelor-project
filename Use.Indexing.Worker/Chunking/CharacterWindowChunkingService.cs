using Microsoft.Extensions.Options;
using Use.Indexing.Worker.Configuration;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Chunking;

/// <summary>
/// Character-window chunker with overlap. A simple, deterministic baseline
/// that's good enough for a prototype. For production, consider sentence- or
/// token-aware chunking (e.g. using a tokenizer matching the embedding model).
/// </summary>
public sealed class CharacterWindowChunkingService : IChunkingService
{
    private readonly ChunkingOptions _options;

    public CharacterWindowChunkingService(IOptions<IndexingOptions> options)
    {
        _options = options.Value.Chunking;
    }

    public IReadOnlyList<DocumentChunk> Chunk(NormalizedDocument document)
    {
        var text = document.PlainText;
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<DocumentChunk>();

        var size = Math.Max(200, _options.TargetCharacters);
        var overlap = Math.Clamp(_options.Overlap, 0, size / 2);
        var step = size - overlap;

        var chunks = new List<DocumentChunk>();
        var order = 0;

        for (var start = 0; start < text.Length; start += step)
        {
            var length = Math.Min(size, text.Length - start);
            var slice = text.Substring(start, length);

            var chunkId = $"{document.Reference.SourceSystem}:{document.Reference.SourceDocumentId}:{order}";
            var meta = new Dictionary<string, string>(document.Metadata)
            {
                ["title"] = document.Title,
                ["chunkOrder"] = order.ToString(),
                ["sourceUrl"] = document.Reference.Url
            };

            chunks.Add(new DocumentChunk(chunkId, document.Reference, order, slice, meta));
            order++;

            if (start + length >= text.Length) break;
        }

        return chunks;
    }
}

