using System.Security.Cryptography;
using System.Text;
using MongoDB.Driver;
using Nakshatra.Shared;
using Nakshatra.Shared.Caching;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Catalog.Service;

public sealed record ProductWriteResult(Product? Product, bool Conflict = false, int PreviousStock = int.MaxValue);

public sealed class CatalogProductWrites(IDocumentRepository<Product> products, ICacheService cache, IServiceProvider services)
{
    internal static readonly SemaphoreSlim MemoryLock = new(1, 1);
    private readonly IMongoDatabase? database = products is MongoDocumentRepository<Product> ? services.GetService<IMongoDatabase>() : null;

    public Task<ProductWriteResult> UpdateAsync(string id, Product incoming, CancellationToken ct = default) =>
        WriteAsync(id, incoming, 0, null, ct);

    public Task<ProductWriteResult> AdjustAsync(string id, int delta, string? operationId, CancellationToken ct = default) =>
        WriteAsync(id, null, delta, operationId, ct);

    private async Task<ProductWriteResult> WriteAsync(string id, Product? incoming, int delta, string? operationId, CancellationToken ct)
    {
        var key = string.IsNullOrEmpty(operationId) ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operationId)));
        if (database is null) await MemoryLock.WaitAsync(ct);
        try
        {
            while (true)
            {
                var previous = await products.GetAsync(id, ct);
                if (previous is null) return new(null);
                if (key is not null && previous.StockReleaseOperations.TryGetValue(key, out var recorded))
                    return new(previous, recorded != delta);

                var stockLong = incoming is null ? (long)previous.Stock + delta : incoming.Stock;
                if (stockLong < 0 && incoming is null || stockLong > int.MaxValue)
                    return new(previous, true);
                var stock = (int)stockLong;
                var restocked = previous.Stock <= 0 && stock > 0;
                Product saved;
                if (database is null)
                {
                    saved = Copy(previous);
                    if (incoming is not null) CopyEditable(incoming, saved);
                    saved.Stock = stock;
                    if (key is not null) saved.StockReleaseOperations[key] = delta;
                    if (restocked)
                    {
                        saved.BackInStockVersion++;
                        saved.BackInStockProductName = saved.Name;
                        saved.BackInStockQuantity = stock;
                    }
                    if (!await products.TryReplaceAsync(p => p.Id == id, saved, ct)) continue;
                }
                else
                {
                    var collection = database.GetCollection<Product>(ServiceDefaults.CollectionNameFor(typeof(Product)));
                    var f = Builders<Product>.Filter;
                    var version = f.Eq(p => p.BackInStockVersion, previous.BackInStockVersion);
                    if (previous.BackInStockVersion == 0) version |= f.Exists(p => p.BackInStockVersion, false);
                    var stockMatches = f.Eq(p => p.Stock, previous.Stock);
                    if (previous.Stock == 0) stockMatches |= f.Exists(p => p.Stock, false);
                    var filter = f.Eq(p => p.Id, id) & stockMatches & version;
                    if (key is not null) filter &= f.Exists($"StockReleaseOperations.{key}", false);
                    var updates = new List<UpdateDefinition<Product>> { Builders<Product>.Update.Set(p => p.Stock, stock) };
                    if (incoming is not null)
                    {
                        var u = Builders<Product>.Update;
                        updates.AddRange(new[] { u.Set(p => p.Name, incoming.Name), u.Set(p => p.Description, incoming.Description),
                            u.Set(p => p.Category, incoming.Category), u.Set(p => p.Price, incoming.Price),
                            u.Set(p => p.Currency, incoming.Currency), u.Set(p => p.VendorId, incoming.VendorId),
                            u.Set(p => p.ImageUrl, incoming.ImageUrl), u.Set(p => p.Tags, incoming.Tags),
                            u.Set(p => p.ListPrice, incoming.ListPrice) });
                    }
                    if (key is not null) updates.Add(Builders<Product>.Update.Set($"StockReleaseOperations.{key}", delta));
                    if (restocked)
                    {
                        updates.Add(Builders<Product>.Update.Set(p => p.BackInStockVersion, previous.BackInStockVersion + 1));
                        updates.Add(Builders<Product>.Update.Set(p => p.BackInStockProductName, incoming?.Name ?? previous.Name));
                        updates.Add(Builders<Product>.Update.Set(p => p.BackInStockQuantity, stock));
                    }
                    saved = await collection.FindOneAndUpdateAsync(filter, Builders<Product>.Update.Combine(updates),
                        new FindOneAndUpdateOptions<Product> { ReturnDocument = ReturnDocument.After }, ct);
                    if (saved is null) continue;
                }
                await cache.RemoveAsync("product:all", ct);
                await cache.RemoveAsync($"product:{id}", ct);
                return new(saved, false, previous.Stock);
            }
        }
        finally { if (database is null) MemoryLock.Release(); }
    }

    private static void CopyEditable(Product from, Product to)
    {
        to.Name = from.Name; to.Description = from.Description; to.Category = from.Category;
        to.Price = from.Price; to.Currency = from.Currency; to.VendorId = from.VendorId;
        to.ImageUrl = from.ImageUrl; to.Tags = new List<string>(from.Tags); to.ListPrice = from.ListPrice;
    }

    internal static Product Copy(Product product)
    {
        var copy = new Product
        {
            Id = product.Id, Stock = product.Stock, CreatedAt = product.CreatedAt,
            AverageRating = product.AverageRating, ReviewCount = product.ReviewCount,
            BackInStockVersion = product.BackInStockVersion, BackInStockProductName = product.BackInStockProductName,
            BackInStockQuantity = product.BackInStockQuantity,
            StockReleaseOperations = new Dictionary<string, int>(product.StockReleaseOperations)
        };
        CopyEditable(product, copy);
        return copy;
    }
}
