using MongoDB.Driver;
using Nakshatra.Shared;
using Nakshatra.Shared.Caching;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Catalog.Service;

public sealed class ProductRatings(
    IDocumentRepository<Product> products,
    IDocumentRepository<Review> reviews,
    ICacheService cache,
    IServiceProvider services)
{
    private readonly IMongoDatabase? database = products is MongoDocumentRepository<Product> ? services.GetService<IMongoDatabase>() : null;

    public async Task BackfillAsync(CancellationToken ct = default)
    {
        foreach (var product in await products.ListAsync(ct))
            await RefreshAsync(product.Id, ct);
        await cache.RemoveAsync("product:all", ct);
    }

    public async Task RefreshAsync(string productId, CancellationToken ct = default)
    {
        if (database is null)
        {
            await CatalogProductWrites.MemoryLock.WaitAsync(ct);
            try
            {
                var product = await products.GetAsync(productId, ct);
                if (product is null) return;
                var productReviews = await reviews.FindAsync(r => r.ProductId == productId, ct);
                var updated = CatalogProductWrites.Copy(product);
                updated.ReviewCount = productReviews.Count;
                updated.AverageRating = Average(productReviews);
                await products.TryReplaceAsync(p => p.Id == productId, updated, ct);
            }
            finally { CatalogProductWrites.MemoryLock.Release(); }
        }
        else
        {
            var collection = database.GetCollection<Product>(ServiceDefaults.CollectionNameFor(typeof(Product)));
            var filter = Builders<Product>.Filter;
            while (true)
            {
                var product = await products.GetAsync(productId, ct);
                if (product is null) return;
                var productReviews = await reviews.FindAsync(r => r.ProductId == productId, ct);
                // Legacy documents may not yet have either aggregate field.
                var countMatches = filter.Eq(p => p.ReviewCount, product.ReviewCount);
                var ratingMatches = filter.Eq(p => p.AverageRating, product.AverageRating);
                if (product.ReviewCount == 0) countMatches |= filter.Exists(p => p.ReviewCount, false);
                if (product.AverageRating == 0) ratingMatches |= filter.Exists(p => p.AverageRating, false);
                var result = await collection.UpdateOneAsync(
                    filter.Eq(p => p.Id, productId) & countMatches & ratingMatches,
                    Builders<Product>.Update.Set(p => p.ReviewCount, productReviews.Count)
                        .Set(p => p.AverageRating, Average(productReviews)), cancellationToken: ct);
                if (result.MatchedCount != 0) break;
            }
        }

        // Invalidate rather than write through: another refresh may have completed meanwhile.
        await cache.RemoveAsync($"product:{productId}", ct);
        await cache.RemoveAsync("product:all", ct);
    }

    private static double Average(IReadOnlyList<Review> reviews) =>
        reviews.Count == 0 ? 0 : Math.Round(reviews.Average(r => r.Rating), 2);
}
