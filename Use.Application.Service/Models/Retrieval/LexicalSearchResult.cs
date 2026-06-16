namespace Use.Application.Service.Models.Retrieval;

/// <summary>
/// One chunk returned by the PostgreSQL lexical (full-text) search, normalised
/// into the same logical shape as <see cref="RetrievedChunk"/> so it can be
/// fused with vector results.
/// </summary>
public sealed record LexicalSearchResult(
    string ChunkId,
    string SourceSystem,
    string SourceDocumentId,
    int ChunkOrder,
    string? Title,
    string? Url,
    string? HeadingPath,
    string Text,
    double Score);

