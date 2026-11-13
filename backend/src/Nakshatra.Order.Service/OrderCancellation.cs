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
        IEventPublisher publisher,
        CatalogClient catalog,
        Order order,
        string note,
        CancellationToken ct = default)
    {
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        var restock = !order.HasShipped();
        await OrderStatusUpdater.ApplyAsync(repo, publisher, order, OrderStatus.Cancelled, note, ct);

        if (restock)
        {
            await catalog.ReleaseStockAsync(order.Items);
        }

        return order;
    }
}
