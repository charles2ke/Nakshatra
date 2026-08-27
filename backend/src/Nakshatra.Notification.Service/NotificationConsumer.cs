using System.Text.Json;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Notification.Service;

/// <summary>
/// Turns a domain event into a notification. Kept as pure logic so it is easy to unit test and to
/// extend with new topics.
/// </summary>
public static class NotificationComposer
{
    public static Notification? Compose(EventEnvelope envelope)
    {
        using var document = JsonDocument.Parse(envelope.Payload);
        var root = document.RootElement;

        return envelope.Topic switch
        {
            Topics.OrdersCreated => new Notification
            {
                UserId = GetString(root, "userId"),
                Title = "Order received",
                Body = $"We have received your order {envelope.Key} and are getting it ready.",
                Reference = envelope.Key,
                Topic = envelope.Topic,
                Channel = NotificationChannel.Email
            },
            Topics.OrdersStatusChanged => new Notification
            {
                UserId = GetString(root, "userId"),
                Title = $"Order {GetString(root, "status")}",
                Body = $"Order {envelope.Key} is now {GetString(root, "status")}.",
                Reference = envelope.Key,
                Topic = envelope.Topic
            },
            Topics.PaymentsCompleted => new Notification
            {
                UserId = GetString(root, "userId"),
                Title = GetString(root, "status") == "Captured" ? "Payment successful" : "Payment issue",
                Body = GetString(root, "status") == "Captured"
                    ? $"Your payment for order {envelope.Key} was captured."
                    : $"Your payment for order {envelope.Key} did not go through.",
                Reference = envelope.Key,
                Topic = envelope.Topic,
                Channel = NotificationChannel.Email,
                Severity = GetString(root, "status") == "Captured" ? NotificationSeverity.Info : NotificationSeverity.Warning
            },
            Topics.SupplyChainEventRecorded => new Notification
            {
                UserId = GetString(root, "userId"),
                Title = $"Shipment update: {GetString(root, "stage")}",
                Body = GetString(root, "description"),
                Reference = envelope.Key,
                Topic = envelope.Topic
            },
            Topics.InventoryLow => new Notification
            {
                Audience = "Vendor",
                Title = "Low stock",
                Body = $"Product {envelope.Key} is below its reorder point. Suggested order: {GetNumber(root, "recommendedOrderQuantity")} unit(s).",
                Reference = envelope.Key,
                Topic = envelope.Topic,
                Severity = NotificationSeverity.Warning
            },
            Topics.InventoryReplenishmentOrdered => new Notification
            {
                Audience = "Vendor",
                Title = "Replenishment ordered",
                Body = $"{GetNumber(root, "quantity")} unit(s) of {envelope.Key} are on the way.",
                Reference = envelope.Key,
                Topic = envelope.Topic
            },
            Topics.MediaTranscoded => new Notification
            {
                Audience = "Admin",
                Title = "Media ready",
                Body = $"Media asset {envelope.Key} finished transcoding with status {GetString(root, "status")}.",
                Reference = envelope.Key,
                Topic = envelope.Topic
            },
            _ => null
        };
    }

    private static string GetString(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string GetNumber(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.ToString()
            : "0";
}

/// <summary>
/// Subscribes to every notification-worthy topic and persists the resulting notifications.
/// Delivery to email/SMS providers is represented by a pluggable dispatcher.
/// </summary>
public class NotificationConsumer : BackgroundService
{
    private static readonly string[] SubscribedTopics =
    {
        Topics.OrdersCreated,
        Topics.OrdersStatusChanged,
        Topics.PaymentsCompleted,
        Topics.SupplyChainEventRecorded,
        Topics.InventoryLow,
        Topics.InventoryReplenishmentOrdered,
        Topics.MediaTranscoded
    };

    private readonly IEventSubscriber _subscriber;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotificationConsumer> _logger;

    public NotificationConsumer(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<NotificationConsumer> logger)
    {
        _subscriber = subscriber;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(SubscribedTopics.Select(topic =>
            _subscriber.SubscribeAsync(topic, "notification-service", async (envelope, ct) =>
            {
                try
                {
                    var notification = NotificationComposer.Compose(envelope);
                    if (notification is null)
                    {
                        return;
                    }

                    using var scope = _serviceProvider.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository<Notification>>();
                    await repo.UpsertAsync(notification, ct);

                    await scope.ServiceProvider.GetRequiredService<INotificationDispatcher>().DispatchAsync(notification, ct);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Could not parse the payload for topic {Topic}.", envelope.Topic);
                }
            }, stoppingToken)));
}

/// <summary>Delivers a notification over its channel.</summary>
public interface INotificationDispatcher
{
    Task DispatchAsync(Notification notification, CancellationToken ct = default);
}

/// <summary>
/// Default dispatcher. Email/SMS providers are not wired up in this repository, so delivery is
/// logged without any personal data and the notification stays available in the in-app feed.
/// </summary>
public class LoggingNotificationDispatcher : INotificationDispatcher
{
    private readonly ILogger<LoggingNotificationDispatcher> _logger;

    public LoggingNotificationDispatcher(ILogger<LoggingNotificationDispatcher> logger) => _logger = logger;

    public Task DispatchAsync(Notification notification, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Dispatched {Channel} notification {NotificationId} for topic {Topic} (reference {Reference}).",
            notification.Channel, notification.Id, notification.Topic, notification.Reference);
        return Task.CompletedTask;
    }
}
