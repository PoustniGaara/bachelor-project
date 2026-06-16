namespace Use.Indexing.Worker.Models;

/// <summary>
/// A chunk of a normalized document.
///
/// <para>
/// <see cref="Text"/> is the clean, human-readable content used for display
/// and as the final RAG context passed to the LLM. <see cref="EmbeddingText"/>
/// is an enriched variant (with title, path, heading-path, etc. prepended)
/// used only as input to the embedding model — it improves retrieval
/// precision but must not be shown to end users. When <see cref="EmbeddingText"/>
/// is null, callers fall back to <see cref="Text"/>.
/// </para>
/// </summary>
public sealed record DocumentChunk(
    string ChunkId,
    SourceDocumentReference Reference,
    int Order,
    string Text,
    IReadOnlyDictionary<string, string> Metadata,
    string? EmbeddingText = null);
