namespace Use.Indexing.Worker.Models;

/// <summary>
/// A single hit from the PostgreSQL lexical (full-text / BM25-style) search
/// over <c>rag_document_chunks</c>. Carries enough to identify the chunk in
/// both stores (the shared <see cref="ChunkId"/>) plus a relevance
/// <see cref="Rank"/> and a short context for display.
/// </summary>
public sealed record LexicalChunkSearchResult(
    string ChunkId,
    string SourceSystem,
    string SourceDocumentId,
    int ChunkOrder,
    string Title,
    string? HeadingPath,
    string Text,
    double Rank);

