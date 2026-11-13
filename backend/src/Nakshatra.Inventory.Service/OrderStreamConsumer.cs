using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Inventory.Service;

/// <summary>
/// Records demand from the <c>orders.created</c> stream: reserves stock and appends to the daily
/// sales history that feeds the forecast.
/// </summary>
public class OrderStreamConsumer : EventConsumerBackgroundService
{
    public OrderStreamConsumer(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<OrderStreamConsumer> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.OrdersCreated;

    protected override string ConsumerGroup => "inventory-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var order = envelope.As<Order>();
        if (order is null)
        {
            return;
        }

        var repo = scopedServices.GetRequiredService<IDocumentRepository<InventoryItem>>();
        var publisher = scopedServices.GetRequiredService<IEventPublisher>();

        foreach (var line in order.Items)
        {
            var item = await repo.GetAsync(line.ProductId, ct)
                       ?? new InventoryItem { Id = line.ProductId, ProductId = line.ProductId };

            InventoryMath.RecordSale(item, DateOnly.FromDateTime(order.CreatedAt), line.Quantity);
            await repo.UpsertAsync(item, ct);

            var forecast = DemandForecaster.Forecast(item);
            if (forecast.ReorderNow)
            {
                // Surfaces a replenishment alert to the notification service.
                await publisher.PublishAsync(Topics.InventoryLow, item.ProductId, new
                {
                    productId = item.ProductId,
                    vendorId = item.VendorId,
                    available = item.Available,
                    reorderPoint = forecast.ReorderPoint,
                    recommendedOrderQuantity = forecast.RecommendedOrderQuantity
                }, ct);
            }
        }
    }
}

/// <summary>
/// Reverses the recorded sale when an order is cancelled on <c>orders.status-changed</c> before it
/// shipped, so on-hand stock is restored and the forecast is not inflated by demand that never left.
/// </summary>
public class CancelledOrderStreamConsumer : EventConsumerBackgroundService
{
    public CancelledOrderStreamConsumer(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<CancelledOrderStreamConsumer> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.OrdersStatusChanged;

    protected override string ConsumerGroup => "inventory-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var order = envelope.As<Order>();
        if (order is not { Status: OrderStatus.Cancelled } || order.HasShipped())
        {
            return;
        }

        var repo = scopedServices.GetRequiredService<IDocumentRepository<InventoryItem>>();
        foreach (var line in order.Items)
        {
            if (await repo.GetAsync(line.ProductId, ct) is not { } item)
            {
                continue;
            }

            InventoryMath.ReverseSale(item, DateOnly.FromDateTime(order.CreatedAt), line.Quantity);
            await repo.UpsertAsync(item, ct);
        }
    }
}

public static class InventoryMath
{
    /// <summary>Applies a sale: reduces on-hand stock and appends to the day's sales bucket.</summary>
    public static void RecordSale(InventoryItem item, DateOnly date, int quantity)
    {
        item.OnHand = Math.Max(item.OnHand - quantity, 0);

        var point = item.SalesHistory.FirstOrDefault(p => p.Date == date);
        if (point is null)
        {
            item.SalesHistory.Add(new SalesDataPoint { Date = date, Units = quantity });
        }
        else
        {
            point.Units += quantity;
        }

        // Keep two years of daily history; older points add little forecasting value.
        if (item.SalesHistory.Count > 730)
        {
            item.SalesHistory = item.SalesHistory.OrderByDescending(p => p.Date).Take(730).OrderBy(p => p.Date).ToList();
        }

        item.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Undoes <see cref="RecordSale"/> for a cancelled order: restocks and removes the units sold.</summary>
    public static void ReverseSale(InventoryItem item, DateOnly date, int quantity)
    {
        item.OnHand += quantity;

        var point = item.SalesHistory.FirstOrDefault(p => p.Date == date);
        if (point is not null)
        {
            point.Units = Math.Max(point.Units - quantity, 0);
            if (point.Units == 0)
            {
                item.SalesHistory.Remove(point);
            }
        }

        item.UpdatedAt = DateTime.UtcNow;
    }
}
