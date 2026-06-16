namespace Use.Indexing.Worker.Models;

/// <summary>
/// A chunk paired with its embedding vector and the model that produced it.
/// Storing the model+dimension allows safe re-embedding migrations later.
/// </summary>
public sealed record EmbeddedChunk(
    DocumentChunk Chunk,
    ReadOnlyMemory<float> Embedding,
    string Model,
    int Dimensions);

