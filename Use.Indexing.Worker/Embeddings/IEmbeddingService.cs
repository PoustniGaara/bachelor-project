using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Embeddings;

/// <summary>
/// Embedding-provider abstraction. Concrete implementations may call OpenAI,
/// Azure OpenAI, a self-hosted model, etc. Returning <see cref="EmbeddedChunk"/>
/// keeps model+dimension info bound to each vector for safe migrations.
/// </summary>
public interface IEmbeddingService
{
    Task<IReadOnlyList<EmbeddedChunk>> EmbedAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken);
}

