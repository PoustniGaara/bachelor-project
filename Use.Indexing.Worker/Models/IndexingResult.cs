namespace Use.Indexing.Worker.Models;

/// <summary>
/// Aggregate result of an indexing cycle, used for logging/metrics.
/// </summary>
public sealed record IndexingResult(
    int DocumentsDiscovered,
    int DocumentsIndexed,
    int DocumentsFailed,
    int ChunksWritten,
    TimeSpan Duration);

