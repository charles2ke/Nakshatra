using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Models;

namespace Nakshatra.Catalog.Service;

/// <summary>A shopper's question about a product, answered by other shoppers or the seller.</summary>
public class ProductQuestion : IEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProductId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ProductAnswer> Answers { get; set; } = new();
}

public class ProductAnswer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsSeller { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>"Notify me when available" subscription for an out-of-stock product.</summary>
public class StockAlert : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long SubscribedVersion { get; set; }
    public string? NotificationId { get; set; }
    public string? PendingProductName { get; set; }
    public int? PendingStock { get; set; }
    public string? LeaseId { get; set; }
    public DateTime? LeaseUntil { get; set; }

    // One alert per shopper and product keeps subscribing idempotent.
    public static string IdFor(string productId, string userId) => $"{productId}:{userId}";
}

public record HelpfulVoteRequest(string? UserId);

public record AskQuestionRequest(string? UserId, string? UserName, string? Text);

public record AnswerQuestionRequest(string? UserId, string? UserName, string? Text, string? Persona);

public record StockAlertRequest(string? UserId);

public static class CatalogRules
{
    public const int MaxQuestionLength = 500;

    public static readonly TimeSpan ProductTtl = TimeSpan.FromMinutes(10);

    /// <summary>Reviews by shoppers with a paid order containing the product are marked as verified purchases.</summary>
    public static async Task<bool> IsVerifiedPurchaseAsync(OrderClient orders, string userId, string productId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var userOrders = await orders.GetOrdersForUserAsync(userId, ct);
        return userOrders?.Any(order =>
            (order.Status is OrderStatus.Paid or OrderStatus.Packed or OrderStatus.Shipped or OrderStatus.Delivered
                or OrderStatus.ReturnRequested or OrderStatus.Returned)
            && order.Items.Any(item => item.ProductId == productId)) == true;
    }

}
