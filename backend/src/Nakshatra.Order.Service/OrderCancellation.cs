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
    public static Task<Order> CancelAsync(
        IDocumentRepository<Order> repo,
        IDocumentRepository<OrderCancellationWork> workRepo,
        IEventPublisher publisher,
        CatalogClient catalog,
        Order order,
        string note,
        CancellationToken ct = default) =>
        OrderStockReturn.RunAsync(repo, workRepo, publisher, catalog, order, () =>
        {
            var cancelledOrder = OrderSnapshot.Copy(order);
            if (cancelledOrder.Status != OrderStatus.Cancelled)
            {
                cancelledOrder.Status = OrderStatus.Cancelled;
                cancelledOrder.History.Add(new OrderStatusChange { Status = OrderStatus.Cancelled, Note = note });
            }

            return new OrderCancellationWork
            {
                Id = order.Id,
                Order = cancelledOrder,
                Restock = !order.HasShipped(),
                ReleasedLines = new List<bool>(new bool[cancelledOrder.Items.Count])
            };
        }, ct);
}

/// <summary>
/// Durable progress of a status change that may hand stock back to the catalog (a cancellation or a
/// completed return). Retrying with the same work document finishes the job without repeating steps.
/// </summary>
public interface IOrderStockReturnWork : IEntity
{
    Order Order { get; set; }
    bool Restock { get; set; }
    bool StatusChangedPublished { get; set; }
    List<bool> ReleasedLines { get; set; }
}

public static class OrderStockReturn
{
    /// <summary>
    /// Stores the order in its new state, streams the change once and releases each line's stock once.
    /// The work document is created on the first attempt and reused by every retry.
    /// </summary>
    public static async Task<Order> RunAsync<TWork>(
        IDocumentRepository<Order> repo,
        IDocumentRepository<TWork> workRepo,
        IEventPublisher publisher,
        CatalogClient catalog,
        Order order,
        Func<TWork> createWork,
        CancellationToken ct)
        where TWork : class, IOrderStockReturnWork
    {
        var work = await workRepo.GetAsync(order.Id, ct);
        if (work is null)
        {
            work = createWork();
            if (!await workRepo.TryCreateAsync(work, ct))
            {
                work = await workRepo.GetAsync(order.Id, ct)
                       ?? throw new InvalidOperationException($"Status change work for order '{order.Id}' could not be loaded.");
            }
        }

        // Once the new status is stored, a retry must not overwrite later changes such as a refund note.
        var current = order;
        if (order.Status != work.Order.Status)
        {
            await repo.UpsertAsync(work.Order, ct);
            current = work.Order;
        }

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

        return current;
    }
}

public static class OrderSnapshot
{
    /// <summary>Deep copy, so a work document's snapshot never shares lists with the live order.</summary>
    public static Order Copy(Order order) => new()
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
        }).ToList(),
        Discount = order.Discount,
        CouponCode = order.CouponCode,
        DeliverySpeed = order.DeliverySpeed,
        ShippingFee = order.ShippingFee,
        EstimatedDelivery = order.EstimatedDelivery,
        GiftWrap = order.GiftWrap,
        GiftWrapFee = order.GiftWrapFee,
        GiftMessage = order.GiftMessage,
        BuyNow = order.BuyNow,
        ReturnReason = order.ReturnReason
    };
}

public class OrderCancellationWork : IOrderStockReturnWork
{
    public string Id { get; set; } = string.Empty;
    public Order Order { get; set; } = new();
    public bool Restock { get; set; }
    public bool StatusChangedPublished { get; set; }
    public List<bool> ReleasedLines { get; set; } = new();
}
