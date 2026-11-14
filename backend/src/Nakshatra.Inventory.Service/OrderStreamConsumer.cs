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

        foreach (var lines in order.Items.GroupBy(line => line.ProductId))
        {
            var item = await repo.GetAsync(lines.Key, ct)
                       ?? new InventoryItem { Id = lines.Key, ProductId = lines.Key };
            item.OrderSales ??= new List<InventoryOrderSale>();
            if (item.OrderSales.Any(sale => sale.OrderId == order.Id))
            {
                continue;
            }

            var quantity = lines.Sum(line => line.Quantity);
            var stockDecrement = InventoryMath.RecordSale(item, DateOnly.FromDateTime(order.CreatedAt), quantity);
            item.OrderSales.Add(new InventoryOrderSale
            {
                OrderId = order.Id,
                Quantity = quantity,
                StockDecrement = stockDecrement
            });
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
        foreach (var lines in order.Items.GroupBy(line => line.ProductId))
        {
            var item = await repo.GetAsync(lines.Key, ct)
                       ?? new InventoryItem { Id = lines.Key, ProductId = lines.Key };
            item.OrderSales ??= new List<InventoryOrderSale>();
            var sale = item.OrderSales.FirstOrDefault(sale => sale.OrderId == order.Id);
            if (sale is null)
            {
                item.OrderSales.Add(new InventoryOrderSale
                {
                    OrderId = order.Id,
                    Quantity = lines.Sum(line => line.Quantity),
                    Cancelled = true
                });
            }
            else if (!sale.Cancelled)
            {
                InventoryMath.ReverseSale(item, DateOnly.FromDateTime(order.CreatedAt), sale.Quantity, sale.StockDecrement);
                sale.Cancelled = true;
            }

            await repo.UpsertAsync(item, ct);
        }
    }
}

public static class InventoryMath
{
    /// <summary>Applies a sale: reduces on-hand stock and appends to the day's sales bucket.</summary>
    public static int RecordSale(InventoryItem item, DateOnly date, int quantity)
    {
        var stockDecrement = Math.Min(Math.Max(item.OnHand, 0), quantity);
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
        return stockDecrement;
    }

    /// <summary>Undoes <see cref="RecordSale"/> for a cancelled order: restocks and removes the units sold.</summary>
    public static void ReverseSale(InventoryItem item, DateOnly date, int quantity)
        => ReverseSale(item, date, quantity, quantity);

    public static void ReverseSale(InventoryItem item, DateOnly date, int quantity, int stockDecrement)
    {
        item.OnHand += stockDecrement;

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
