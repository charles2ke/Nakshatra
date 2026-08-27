using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nakshatra.Notification.Service;
using Nakshatra.Shared;
using Nakshatra.Shared.Messaging;

namespace Nakshatra.Tests;

/// <summary>
/// The notification host is shared across the class. The in-process event bus keeps its handlers in
/// static state, so booting the service twice would deliver every event to both hosts.
/// </summary>
public class NotificationHostFixture : IDisposable
{
    private readonly WebApplicationFactory<Nakshatra.Notification.Service.ServiceMarker> _factory = new();

    public NotificationHostFixture() => Client = _factory.CreateClient();

    public HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class SupplyChainAndNotificationTests : IClassFixture<NotificationHostFixture>
{
    private readonly NotificationHostFixture _notifications;

    public SupplyChainAndNotificationTests(NotificationHostFixture notifications) => _notifications = notifications;

    private static HttpClient CreateClient<TMarker>() where TMarker : class
        => new WebApplicationFactory<TMarker>().CreateClient();

    [Fact]
    public async Task Supply_chain_records_a_checkpoint_and_builds_a_timeline()
    {
        using var client = CreateClient<Nakshatra.SupplyChain.Service.ServiceMarker>();
        var reference = $"order-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync($"/api/supplychain/traces/{reference}/events", new
        {
            stage = "Packed",
            description = "Packed at the fulfillment centre",
            location = "FC-1",
            userId = "user-shopper"
        }, ServiceDefaults.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        await client.PostAsJsonAsync($"/api/supplychain/traces/{reference}/events", new
        {
            stage = "InTransit",
            description = "Left the hub",
            location = "Hub-9"
        }, ServiceDefaults.JsonOptions);

        var trace = await client.GetFromJsonAsync<JsonElement>($"/api/supplychain/traces/{reference}", ServiceDefaults.JsonOptions);

        Assert.Equal("InTransit", trace.GetProperty("currentStage").GetString());
        Assert.Equal(2, trace.GetProperty("events").GetArrayLength());
    }

    [Fact]
    public async Task Supply_chain_rejects_an_unknown_stage()
    {
        using var client = CreateClient<Nakshatra.SupplyChain.Service.ServiceMarker>();

        var response = await client.PostAsJsonAsync("/api/supplychain/traces/order-1/events", new { stage = "Teleported" }, ServiceDefaults.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Supply_chain_returns_not_found_for_an_unknown_reference()
    {
        using var client = CreateClient<Nakshatra.SupplyChain.Service.ServiceMarker>();

        var response = await client.GetAsync($"/api/supplychain/traces/missing-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void Composer_maps_an_order_event_to_a_customer_notification()
    {
        var envelope = new EventEnvelope(
            Topics.OrdersStatusChanged,
            "order-42",
            JsonSerializer.Serialize(new { userId = "user-1", status = "Shipped" }));

        var notification = NotificationComposer.Compose(envelope);

        Assert.NotNull(notification);
        Assert.Equal("user-1", notification!.UserId);
        Assert.Contains("Shipped", notification.Title, StringComparison.Ordinal);
        Assert.False(notification.Read);
    }

    [Fact]
    public void Composer_flags_a_failed_payment_as_a_warning()
    {
        var envelope = new EventEnvelope(
            Topics.PaymentsCompleted,
            "order-42",
            JsonSerializer.Serialize(new { userId = "user-1", status = "Declined" }));

        var notification = NotificationComposer.Compose(envelope);

        Assert.Equal(NotificationSeverity.Warning, notification!.Severity);
    }

    [Fact]
    public void Composer_targets_vendors_for_low_stock()
    {
        var envelope = new EventEnvelope(
            Topics.InventoryLow,
            "prod-chair",
            JsonSerializer.Serialize(new { productId = "prod-chair", recommendedOrderQuantity = 120 }));

        var notification = NotificationComposer.Compose(envelope);

        Assert.Equal("Vendor", notification!.Audience);
        Assert.Contains("120", notification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Composer_ignores_topics_it_does_not_handle()
    {
        var envelope = new EventEnvelope(Topics.CartItemAdded, "cart-1", "{}");

        Assert.Null(NotificationComposer.Compose(envelope));
    }

    [Fact]
    public async Task Notifications_can_be_listed_and_marked_read()
    {
        var client = _notifications.Client;
        var userId = $"user-{Guid.NewGuid():N}";

        var envelope = new EventEnvelope(
            Topics.OrdersCreated,
            "order-77",
            JsonSerializer.Serialize(new { userId }));

        var delivered = await client.PostAsJsonAsync("/internal/events", envelope, ServiceDefaults.JsonOptions);
        Assert.True(delivered.IsSuccessStatusCode);

        // The consumer handles the event asynchronously.
        JsonElement feed = default;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            feed = await client.GetFromJsonAsync<JsonElement>($"/api/notifications?userId={userId}", ServiceDefaults.JsonOptions);
            if (feed.GetProperty("items").GetArrayLength() > 0)
            {
                break;
            }

            await Task.Delay(50);
        }

        Assert.Equal(1, feed.GetProperty("unreadCount").GetInt32());
        var id = feed.GetProperty("items")[0].GetProperty("id").GetString();

        var read = await client.PostAsJsonAsync($"/api/notifications/{id}/read", new { }, ServiceDefaults.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/notifications?userId={userId}&unreadOnly=true", ServiceDefaults.JsonOptions);
        Assert.Equal(0, after.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Marking_an_unknown_notification_read_returns_not_found()
    {
        var client = _notifications.Client;

        var response = await client.PostAsJsonAsync($"/api/notifications/{Guid.NewGuid():N}/read", new { }, ServiceDefaults.JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
