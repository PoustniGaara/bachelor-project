using Microsoft.Extensions.Options;
using Use.Indexing.Worker.Configuration;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Embeddings;

/// <summary>
/// Deterministic stub embedding service. Produces a hash-derived vector so the
/// pipeline can run without external API access. Replace with a real provider
/// (OpenAI / Azure OpenAI / local model) before going to production.
/// </summary>
public sealed class StubEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingOptions _options;
    private readonly ILogger<StubEmbeddingService> _logger;

    public StubEmbeddingService(IOptions<IndexingOptions> options, ILogger<StubEmbeddingService> logger)
    {
        _options = options.Value.Embedding;
        _logger = logger;
    }

    public Task<IReadOnlyList<EmbeddedChunk>> EmbedAsync(
        IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        // TODO: Replace with batched call to the configured provider.
        _logger.LogDebug("Stub-embedding {Count} chunks with model {Model}", chunks.Count, _options.Model);

        var dims = Math.Max(8, _options.Dimensions);
        var result = new List<EmbeddedChunk>(chunks.Count);

        foreach (var c in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vec = HashToVector(c.Text, dims);
            result.Add(new EmbeddedChunk(c, vec, _options.Model, dims));
        }

        return Task.FromResult<IReadOnlyList<EmbeddedChunk>>(result);
    }

    private static ReadOnlyMemory<float> HashToVector(string text, int dims)
    {
        var v = new float[dims];
        unchecked
        {
            var seed = (uint)text.GetHashCode();
            for (var i = 0; i < dims; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                v[i] = ((seed & 0xFFFF) / 65535f) * 2f - 1f;
            }
        }
        return v;
    }
}

