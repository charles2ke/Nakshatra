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
        if (order.Status != OrderStatus.Cancelled)
        {
            order.Status = OrderStatus.Cancelled;
            order.History.Add(new OrderStatusChange { Status = OrderStatus.Cancelled, Note = note });
            order.CancellationStatusChangedPublished = false;
            order.CancellationStockReleasedProductIds = new List<string>();
            await repo.UpsertAsync(order, ct);
        }

        if (order.CancellationStockReleasedProductIds is { } releasedProductIds && !order.HasShipped())
        {
            foreach (var lines in order.Items.GroupBy(item => item.ProductId))
            {
                if (releasedProductIds.Contains(lines.Key))
                {
                    continue;
                }

                if (await catalog.ReleaseStockAsync(lines.Key, lines.Sum(item => item.Quantity)))
                {
                    releasedProductIds.Add(lines.Key);
                    await repo.UpsertAsync(order, ct);
                }
            }
        }

        if (order.CancellationStatusChangedPublished != true)
        {
            await publisher.PublishAsync(Topics.OrdersStatusChanged, order.Id, order, ct);
            order.CancellationStatusChangedPublished = true;
            await repo.UpsertAsync(order, ct);
        }

        return order;
    }
}
