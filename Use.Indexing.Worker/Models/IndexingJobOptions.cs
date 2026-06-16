namespace Use.Indexing.Worker.Models;

/// <summary>
/// Per-run options passed to the orchestrator. A scheduled cycle uses defaults;
/// an event-triggered re-index can target a specific source/document.
/// </summary>
public sealed record IndexingJobOptions(
    bool FullReindex = false,
    SourceSystemType? OnlySource = null,
    string? OnlySourceDocumentId = null,
    int? MaxDocuments = null);

