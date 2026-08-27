using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nakshatra.Shared.Messaging;

/// <summary>
/// Base class for services that react to streamed events. Subscribes on start-up and resolves a
/// scoped service provider for each message.
/// </summary>
public abstract class EventConsumerBackgroundService : BackgroundService
{
    private readonly IEventSubscriber _subscriber;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    protected EventConsumerBackgroundService(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger logger)
    {
        _subscriber = subscriber;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected abstract string Topic { get; }

    protected abstract string ConsumerGroup { get; }

    protected abstract Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => _subscriber.SubscribeAsync(Topic, ConsumerGroup, async (envelope, ct) =>
        {
            using var scope = _serviceProvider.CreateScope();
            try
            {
                await HandleAsync(envelope, scope.ServiceProvider, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process event from {Topic}.", Topic);
            }
        }, stoppingToken);
}
