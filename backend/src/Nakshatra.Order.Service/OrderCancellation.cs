using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

/// <summary>
/// The single path for cancelling an order, whether a customer, an admin or a refund triggers it.
/// Records the change, streams it on <c>orders.status-changed</c> (which refunds captured payments and
/// stops fulfillment) and returns the reserved stock to the catalog unless the goods already shipped.
/// </summary>
public static class OrderCancellation
{
    public static async Task<Order> CancelAsync(
        IDocumentRepository<Order> repo,
        IDocumentRepository<OrderCancellationWork> workRepo,
        IEventPublisher publisher,
        CatalogClient catalog,
        Order order,
        string note,
        CancellationToken ct = default)
    {
        var work = await workRepo.GetAsync(order.Id, ct);
        if (work is null)
        {
            var cancelledOrder = CopyOrder(order);
            if (cancelledOrder.Status != OrderStatus.Cancelled)
            {
                cancelledOrder.Status = OrderStatus.Cancelled;
                cancelledOrder.History.Add(new OrderStatusChange { Status = OrderStatus.Cancelled, Note = note });
            }

            var restock = !order.HasShipped();
            work = new OrderCancellationWork
            {
                Id = order.Id,
                Order = cancelledOrder,
                Restock = restock,
                ReleasedLines = new List<bool>(new bool[cancelledOrder.Items.Count])
            };
            if (!await workRepo.TryCreateAsync(work, ct))
            {
                work = await workRepo.GetAsync(order.Id, ct)
                       ?? throw new InvalidOperationException($"Cancellation work for order '{order.Id}' could not be loaded.");
            }
        }

        await repo.UpsertAsync(work.Order, ct);

        if (!work.StatusChangedPublished)
        {
            await publisher.PublishAsync(Topics.OrdersStatusChanged, work.Order.Id, work.Order, ct);
            work.StatusChangedPublished = true;
            await workRepo.UpsertAsync(work, ct);
        }

        var releaseFailed = false;
        if (work.Restock)
        {
            for (var index = 0; index < work.Order.Items.Count; index++)
            {
                if (work.ReleasedLines[index])
                {
                    continue;
                }

                var line = work.Order.Items[index];
                if (!await catalog.ReleaseStockAsync(line.ProductId, line.Quantity, CancellationToken.None))
                {
                    releaseFailed = true;
                    continue;
                }

                work.ReleasedLines[index] = true;
                await workRepo.UpsertAsync(work, CancellationToken.None);
            }
        }

        if (releaseFailed)
        {
            throw new InvalidOperationException($"Stock for order '{order.Id}' could not be fully released.");
        }

        return work.Order;
    }

    private static Order CopyOrder(Order order) => new()
    {
        Id = order.Id,
        UserId = order.UserId,
        Items = order.Items.Select(item => new CartItem
        {
            ProductId = item.ProductId,
            Name = item.Name,
            Price = item.Price,
            Quantity = item.Quantity
        }).ToList(),
        Subtotal = order.Subtotal,
        Tax = order.Tax,
        Total = order.Total,
        Currency = order.Currency,
        Status = order.Status,
        ShippingAddress = order.ShippingAddress,
        CreatedAt = order.CreatedAt,
        History = order.History.Select(change => new OrderStatusChange
        {
            Status = change.Status,
            Timestamp = change.Timestamp,
            Note = change.Note
        }).ToList()
    };
}

public class OrderCancellationWork : IEntity
{
    public string Id { get; set; } = string.Empty;
    public Order Order { get; set; } = new();
    public bool Restock { get; set; }
    public bool StatusChangedPublished { get; set; }
    public List<bool> ReleasedLines { get; set; } = new();
}
