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
}
