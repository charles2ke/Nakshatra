using Nakshatra.Shared.Caching;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Catalog.Service;

public static class CatalogProductEndpoints
{
    public static void MapCatalogProducts(this WebApplication app)
    {
        var group = app.MapGroup("/api/products");
        group.MapGet("/", async (IDocumentRepository<Product> repo, ICacheService cache) =>
            Results.Ok(await cache.GetOrSetAsync("product:all", async () => (await repo.ListAsync()).ToList(), CatalogRules.ProductTtl)));
        group.MapGet("/{id}", async (string id, IDocumentRepository<Product> repo, ICacheService cache) =>
        {
            var product = await cache.GetOrSetAsync($"product:{id}", () => repo.GetAsync(id), CatalogRules.ProductTtl);
            return product is null ? Results.NotFound() : Results.Ok(product);
        });
        group.MapPost("/", async (Product product, IDocumentRepository<Product> repo, ICacheService cache,
            CatalogProductWrites products, BackInStockNotifier notifier) =>
        {
            if (string.IsNullOrWhiteSpace(product.Id)) product.Id = Guid.NewGuid().ToString("N");
            product.AverageRating = 0;
            product.ReviewCount = 0;
            product.CreatedAt = DateTime.UtcNow;
            product.BackInStockVersion = 0;
            product.BackInStockProductName = null;
            product.BackInStockQuantity = 0;
            product.StockReleaseOperations = new();
            while (true)
            {
                if (await repo.TryCreateAsync(product))
                {
                    await cache.RemoveAsync("product:all");
                    await cache.RemoveAsync($"product:{product.Id}");
                    return Results.Created($"/api/products/{product.Id}", product);
                }
                var result = await products.UpdateAsync(product.Id, product);
                if (result.Product is null) continue;
                await notifier.NotifyIfRestockedAsync(result.PreviousStock, result.Product);
                return Results.Created($"/api/products/{product.Id}", result.Product);
            }
        });
        group.MapPut("/{id}", async (string id, Product incoming, CatalogProductWrites products, BackInStockNotifier notifier) =>
        {
            var result = await products.UpdateAsync(id, incoming);
            if (result.Product is null) return Results.NotFound();
            await notifier.NotifyIfRestockedAsync(result.PreviousStock, result.Product);
            return Results.Ok(result.Product);
        });
        group.MapDelete("/{id}", async (string id, IDocumentRepository<Product> repo, ICacheService cache) =>
        {
            var deleted = await repo.DeleteAsync(id);
            await cache.RemoveAsync("product:all");
            await cache.RemoveAsync($"product:{id}");
            return deleted ? Results.NoContent() : Results.NotFound();
        });
    }
}
