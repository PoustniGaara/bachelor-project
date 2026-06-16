using Microsoft.Extensions.Options;
using Use.Indexing.Worker.Configuration;

namespace Use.Indexing.Worker.Orchestration;

/// <summary>
/// Provides the cadence at which the worker should run. A single abstraction
/// keeps the worker agnostic of fixed-interval vs. cron scheduling. The
/// prototype uses a fixed interval; production may swap in a cron parser.
/// </summary>
public interface IIndexingScheduleProvider
{
    TimeSpan StartupDelay { get; }
    TimeSpan GetNextDelay(DateTimeOffset lastRunUtc);
}

/// <summary>Fixed-interval schedule from <see cref="IndexingOptions"/>.</summary>
public sealed class FixedIntervalScheduleProvider : IIndexingScheduleProvider
{
    private readonly IndexingOptions _options;

    public FixedIntervalScheduleProvider(IOptions<IndexingOptions> options) => _options = options.Value;

    public TimeSpan StartupDelay => _options.StartupDelay;

    public TimeSpan GetNextDelay(DateTimeOffset lastRunUtc)
    {
        var elapsed = DateTimeOffset.UtcNow - lastRunUtc;
        var remaining = _options.Interval - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}

