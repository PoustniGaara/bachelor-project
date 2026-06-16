using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Chunking;

/// <summary>
/// Splits a normalized document into retrieval-friendly chunks while
/// preserving order and parent-document relationship.
/// </summary>
public interface IChunkingService
{
    IReadOnlyList<DocumentChunk> Chunk(NormalizedDocument document);
}

