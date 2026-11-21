using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

public record ReturnOrderRequest(string? Reason);

/// <summary>
/// Returns of delivered orders. A customer requests the return within the return window; once the goods
/// are received the return is completed, which restocks every line and streams <see cref="OrderStatus.Returned"/>
/// on <c>orders.status-changed</c> so the payment service refunds the captured payment.
/// </summary>
public static class OrderReturns
{
    public const int ReturnWindowDays = 30;
    public const int MaxReasonLength = 500;

    public static bool IsInReturn(Order order) => order.Status is OrderStatus.ReturnRequested or OrderStatus.Returned;

    /// <summary>When the order was delivered, or <c>null</c> if it never was.</summary>
    public static DateTime? DeliveredAt(Order order) =>
        order.History.LastOrDefault(h => h.Status == OrderStatus.Delivered)?.Timestamp;

    /// <summary>Returns the reason the order cannot be returned, or <c>null</c> when it can.</summary>
    public static string? CheckEligibility(Order order, DateTime now)
    {
        if (order.Status != OrderStatus.Delivered)
        {
            return "Only delivered orders can be returned.";
        }

        var deadline = (DeliveredAt(order) ?? order.CreatedAt).AddDays(ReturnWindowDays);
        return now > deadline ? $"The {ReturnWindowDays}-day return window closed on {deadline:yyyy-MM-dd}." : null;
    }

    public static Task<Order> CompleteAsync(
        IDocumentRepository<Order> repo,
        IDocumentRepository<OrderReturnWork> workRepo,
        IEventPublisher publisher,
        CatalogClient catalog,
        Order order,
        CancellationToken ct) =>
        OrderStockReturn.RunAsync(repo, workRepo, publisher, catalog, order, () =>
        {
            var returnedOrder = OrderSnapshot.Copy(order);
            if (returnedOrder.Status != OrderStatus.Returned)
            {
                returnedOrder.Status = OrderStatus.Returned;
                returnedOrder.History.Add(new OrderStatusChange { Status = OrderStatus.Returned, Note = "Return received; items restocked and refund issued." });
            }

            return new OrderReturnWork
            {
                Id = order.Id,
                Order = returnedOrder,
                // Returned goods go back on the shelf even though the order shipped.
                Restock = true,
                ReleasedLines = new List<bool>(new bool[returnedOrder.Items.Count])
            };
        }, ct);

    /// <summary>Notes the refund of a returned order in its history once, without changing its status.</summary>
    public static async Task RecordRefundAsync(IDocumentRepository<Order> repo, Order order, string paymentId, CancellationToken ct)
    {
        var note = $"Payment {paymentId} refunded.";
        if (order.History.Any(h => h.Note == note))
        {
            return;
        }

        order.History.Add(new OrderStatusChange { Status = order.Status, Note = note });
        await repo.UpsertAsync(order, ct);
    }
}

public class OrderReturnWork : IOrderStockReturnWork
{
    public string Id { get; set; } = string.Empty;
    public Order Order { get; set; } = new();
    public bool Restock { get; set; }
    public bool StatusChangedPublished { get; set; }
    public List<bool> ReleasedLines { get; set; } = new();
}
