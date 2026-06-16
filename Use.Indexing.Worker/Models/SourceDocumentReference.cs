namespace Use.Indexing.Worker.Models;

/// <summary>
/// Lightweight pointer to a document in a source system, returned by a
/// connector's discovery step. Carries just enough metadata to decide whether
/// to (re-)fetch the full document, supporting incremental indexing.
/// </summary>
public sealed record SourceDocumentReference(
    SourceSystemType SourceSystem,
    string SourceDocumentId,
    string Title,
    string Url,
    DateTimeOffset? LastModified,
    string? ETag = null);

