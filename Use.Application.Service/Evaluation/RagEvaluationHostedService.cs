using Microsoft.Extensions.Options;
using Use.Application.Service.Configuration;

namespace Use.Application.Service.Evaluation;

/// <summary>
/// Runs the retrieval evaluation once on startup when
/// <c>Evaluation:EvaluationModeEnabled</c> and <c>Evaluation:RunOnStartup</c>
/// are both true. Completely inert otherwise — the normal chat service is
/// unaffected.
///
/// <para>
/// Because the retrieval services are scoped, the run executes inside a fresh DI
/// scope. The run respects the host's stopping token and can optionally stop the
/// application when finished (<c>Evaluation:StopApplicationAfterEvaluation</c>).
/// </para>
/// </summary>
public sealed class RagEvaluationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly RagEvaluationOptions _options;
    private readonly ILogger<RagEvaluationHostedService> _logger;

    public RagEvaluationHostedService(
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime,
        IOptions<RagEvaluationOptions> options,
        ILogger<RagEvaluationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EvaluationModeEnabled)
        {
            _logger.LogDebug("Evaluation mode disabled (Evaluation:EvaluationModeEnabled=false) — skipping.");
            return;
        }

        if (!_options.RunOnStartup)
        {
            _logger.LogInformation("Evaluation mode enabled but RunOnStartup=false — not running automatically.");
            return;
        }

        // Let the host finish starting before we hammer the retrieval services.
        await WaitForStartupAsync(stoppingToken).ConfigureAwait(false);
        if (stoppingToken.IsCancellationRequested) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<RagEvaluationRunner>();
            await runner.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Evaluation cancelled by host shutdown.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retrieval evaluation failed.");
        }
        finally
        {
            if (_options.StopApplicationAfterEvaluation && !stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("StopApplicationAfterEvaluation=true — shutting the host down.");
                _lifetime.StopApplication();
            }
        }
    }

    private async Task WaitForStartupAsync(CancellationToken stoppingToken)
    {
        var tcs = new TaskCompletionSource();
        await using var registration = _lifetime.ApplicationStarted.Register(() => tcs.TrySetResult());
        await using var cancelRegistration = stoppingToken.Register(() => tcs.TrySetResult());
        await tcs.Task.ConfigureAwait(false);
    }
}

