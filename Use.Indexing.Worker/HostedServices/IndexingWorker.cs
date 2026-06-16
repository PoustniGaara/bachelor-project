using Use.Indexing.Worker.Models;
using Use.Indexing.Worker.Orchestration;

namespace Use.Indexing.Worker.HostedServices;

/// <summary>
/// Periodic hosted service that drives scheduled indexing. Replaces the
/// default template <c>Worker</c> class. Delegates the actual work to
/// <see cref="IIndexingOrchestrator"/> so the hosting concern (timing,
/// cancellation, lifetime) stays separate from pipeline logic.
/// </summary>
public sealed class IndexingWorker : BackgroundService
{
    private readonly IIndexingOrchestrator _orchestrator;
    private readonly IIndexingScheduleProvider _schedule;
    private readonly ILogger<IndexingWorker> _logger;

    public IndexingWorker(
        IIndexingOrchestrator orchestrator,
        IIndexingScheduleProvider schedule,
        ILogger<IndexingWorker> logger)
    {
        _orchestrator = orchestrator;
        _schedule = schedule;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IndexingWorker starting; first run in {Delay}", _schedule.StartupDelay);

        try
        {
            await Task.Delay(_schedule.StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var runStarted = DateTimeOffset.UtcNow;
            try
            {
                await _orchestrator.RunAsync(new IndexingJobOptions(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Swallow at this level so a transient failure does not crash the host.
                _logger.LogError(ex, "Indexing cycle failed; will retry on next interval.");
            }

            var delay = _schedule.GetNextDelay(runStarted);
            _logger.LogDebug("Next indexing cycle in {Delay}", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("IndexingWorker stopping.");
    }
}

