using System.Threading.Channels;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Orchestration;

/// <summary>
/// Extension point for event-triggered re-indexing. External triggers
/// (webhooks, message-bus subscribers, manual admin actions, ...) push
/// <see cref="IndexingJobOptions"/> here; a hosted listener drains the
/// channel and invokes the orchestrator. This decouples the producers of
/// triggers from the indexing pipeline.
/// </summary>
public interface IReindexTriggerHandler
{
    /// <summary>Enqueue a re-index request. Safe to call from any thread.</summary>
    ValueTask TriggerAsync(IndexingJobOptions options, CancellationToken cancellationToken);

    /// <summary>Read enqueued requests as they arrive.</summary>
    IAsyncEnumerable<IndexingJobOptions> ReadAllAsync(CancellationToken cancellationToken);
}

/// <summary>In-process channel-based implementation. Real systems may back this
/// with Azure Service Bus, RabbitMQ, an HTTP webhook controller, etc.</summary>
public sealed class ChannelReindexTriggerHandler : IReindexTriggerHandler
{
    private readonly Channel<IndexingJobOptions> _channel =
        Channel.CreateUnbounded<IndexingJobOptions>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask TriggerAsync(IndexingJobOptions options, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(options, cancellationToken);

    public IAsyncEnumerable<IndexingJobOptions> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}

