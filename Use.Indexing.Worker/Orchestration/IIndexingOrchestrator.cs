using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Orchestration;

/// <summary>
/// Coordinates one indexing run end-to-end:
/// connectors → parse → normalize → chunk → embed → persist.
/// Kept independent from any hosting model so the same orchestrator can be
/// invoked by the scheduled worker or by an event-triggered handler.
/// </summary>
public interface IIndexingOrchestrator
{
    Task<IndexingResult> RunAsync(IndexingJobOptions options, CancellationToken cancellationToken);
}

