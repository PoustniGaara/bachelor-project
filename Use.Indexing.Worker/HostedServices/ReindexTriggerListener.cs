using Use.Indexing.Worker.Orchestration;

namespace Use.Indexing.Worker.HostedServices;

/// <summary>
/// Hosted service that listens for event-triggered re-index requests pushed
/// through <see cref="IReindexTriggerHandler"/> and dispatches them to the
/// orchestrator. This is the seam where future webhook controllers, queue
/// subscribers, or admin endpoints will plug in.
/// </summary>
public sealed class ReindexTriggerListener : BackgroundService
{
    private readonly IReindexTriggerHandler _trigger;
    private readonly IIndexingOrchestrator _orchestrator;
    private readonly ILogger<ReindexTriggerListener> _logger;

    public ReindexTriggerListener(
        IReindexTriggerHandler trigger,
        IIndexingOrchestrator orchestrator,
        ILogger<ReindexTriggerListener> logger)
    {
        _trigger = trigger;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReindexTriggerListener started.");

        try
        {
            await foreach (var options in _trigger.ReadAllAsync(stoppingToken))
            {
                _logger.LogInformation(
                    "Event-triggered re-index received (source={Source}, docId={DocId}, full={Full})",
                    options.OnlySource, options.OnlySourceDocumentId, options.FullReindex);

                try
                {
                    await _orchestrator.RunAsync(options, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Triggered re-index failed.");
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }

        _logger.LogInformation("ReindexTriggerListener stopping.");
    }
}

