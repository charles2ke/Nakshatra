using Nakshatra.Shared;
using Nakshatra.Shared.Caching;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

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

    /// <summary>Recomputes the product's star rating from its reviews and refreshes the cached product.</summary>
    public static async Task RefreshRatingAsync(
        string productId,
        IDocumentRepository<Product> products,
        IDocumentRepository<Review> reviews,
        ICacheService cache,
        CancellationToken ct)
    {
        var productReviews = await reviews.FindAsync(r => r.ProductId == productId, ct);

        // Re-read just before saving to keep the window for overwriting a concurrent stock change small.
        var product = await products.GetAsync(productId, ct);
        if (product is null)
        {
            return;
        }

        product.ReviewCount = productReviews.Count;
        product.AverageRating = productReviews.Count == 0 ? 0 : Math.Round(productReviews.Average(r => r.Rating), 2);
        await products.UpsertAsync(product, ct);
        await cache.RemoveAsync("product:all", ct);
        await cache.SetAsync($"product:{productId}", product, ProductTtl, ct);
    }
}

/// <summary>Tells subscribed shoppers when a product they wanted is available again.</summary>
public sealed class BackInStockNotifier(
    IDocumentRepository<StockAlert> alerts,
    IEventPublisher publisher,
    ILogger<BackInStockNotifier> logger)
{
    /// <summary>
    /// Publishes one <see cref="Topics.ProductsBackInStock"/> event per subscriber when stock rises from zero.
    /// Failures are logged rather than thrown: the stock change is already stored, and failing the request
    /// would make callers retry and restock twice.
    /// </summary>
    public async Task NotifyIfRestockedAsync(int previousStock, Product product)
    {
        if (previousStock > 0 || product.Stock <= 0)
        {
            return;
        }

        try
        {
            // The stock change is already stored, so alerts are sent even if the caller has disconnected.
            var subscribers = await alerts.FindAsync(a => a.ProductId == product.Id, CancellationToken.None);
            foreach (var alert in subscribers)
            {
                await publisher.PublishAsync(Topics.ProductsBackInStock, product.Id, new
                {
                    productId = product.Id,
                    productName = product.Name,
                    userId = alert.UserId,
                    stock = product.Stock
                }, CancellationToken.None);
                await alerts.DeleteAsync(alert.Id, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Back-in-stock alerts for product {ProductId} could not be sent.", LogSanitizer.Sanitize(product.Id));
        }
    }
}
