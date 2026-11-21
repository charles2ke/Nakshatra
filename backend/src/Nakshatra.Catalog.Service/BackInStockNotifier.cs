using MongoDB.Driver;
using System.Security.Cryptography;
using System.Text;
using Nakshatra.Shared;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Catalog.Service;

// Subscriptions are the durable work queue; a claimed alert retains its event snapshot for retries.
public sealed class BackInStockNotifier(
    IDocumentRepository<StockAlert> alerts,
    IDocumentRepository<Product> products,
    IEventPublisher publisher,
    IServiceProvider services,
    ILogger<BackInStockNotifier> logger)
{
    private static readonly SemaphoreSlim MemoryLock = new(1, 1);
    private readonly IMongoDatabase? database = alerts is MongoDocumentRepository<StockAlert> ? services.GetService<IMongoDatabase>() : null;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    public async Task NotifyIfRestockedAsync(int previousStock, Product product)
    {
        if (previousStock <= 0 && product.Stock > 0)
        {
            try { await ProcessAsync(product.Id, CancellationToken.None); }
            catch (Exception ex) { logger.LogError(ex, "Could not scan back-in-stock alerts for {ProductId}.", LogSanitizer.Sanitize(product.Id)); }
        }
    }

    public async Task ProcessAsync(string? productId, CancellationToken ct)
    {
        var candidates = productId is null
            ? await alerts.ListAsync(ct)
            : await alerts.FindAsync(a => a.ProductId == productId, ct);
        foreach (var candidate in candidates)
        {
            try
            {
                var product = await products.GetAsync(candidate.ProductId, ct);
                if (candidate.PendingStock is null && (product is null || product.BackInStockVersion <= candidate.SubscribedVersion))
                    continue;

                var token = Guid.NewGuid().ToString("N");
                var claimed = await ClaimAsync(candidate, product, token, ct);
                if (claimed is null) continue;

                try
                {
                    await publisher.PublishAsync(Topics.ProductsBackInStock, claimed.ProductId, new
                    {
                        eventId = claimed.NotificationId ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{claimed.Id}\n{claimed.CreatedAt.Ticks}"))),
                        productId = claimed.ProductId,
                        productName = claimed.PendingProductName,
                        userId = claimed.UserId,
                        stock = claimed.PendingStock
                    }, ct);
                    await CompleteAsync(claimed.Id, token, true, ct);
                }
                catch
                {
                    await CompleteAsync(claimed.Id, token, false, CancellationToken.None);
                    throw;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Back-in-stock alert {AlertId} could not be sent.", LogSanitizer.Sanitize(candidate.Id));
            }
        }
    }

    private async Task<StockAlert?> ClaimAsync(StockAlert candidate, Product? product, string token, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var name = candidate.PendingProductName ?? product?.BackInStockProductName;
        var stock = candidate.PendingStock ?? product?.BackInStockQuantity;
        if (stock is null or <= 0) return null;
        if (database is not null)
        {
            var collection = database.GetCollection<StockAlert>(ServiceDefaults.CollectionNameFor(typeof(StockAlert)));
            var f = Builders<StockAlert>.Filter;
            var version = f.Eq(a => a.SubscribedVersion, candidate.SubscribedVersion);
            if (candidate.SubscribedVersion == 0) version |= f.Exists(a => a.SubscribedVersion, false);
            return await collection.FindOneAndUpdateAsync(
                f.Eq(a => a.Id, candidate.Id) & f.Eq(a => a.PendingStock, candidate.PendingStock) &
                f.Eq(a => a.CreatedAt, candidate.CreatedAt) & f.Eq(a => a.NotificationId, candidate.NotificationId) & version &
                (f.Eq(a => a.LeaseUntil, null) | f.Lte(a => a.LeaseUntil, now)),
                Builders<StockAlert>.Update.Set(a => a.PendingProductName, name).Set(a => a.PendingStock, stock)
                    .Set(a => a.LeaseId, token).Set(a => a.LeaseUntil, now + LeaseDuration),
                new FindOneAndUpdateOptions<StockAlert> { ReturnDocument = ReturnDocument.After }, ct);
        }

        await MemoryLock.WaitAsync(ct);
        try
        {
            var current = await alerts.GetAsync(candidate.Id, ct);
            if (current is null || current.CreatedAt != candidate.CreatedAt ||
                current.NotificationId != candidate.NotificationId ||
                current.SubscribedVersion != candidate.SubscribedVersion ||
                current.PendingStock != candidate.PendingStock || current.LeaseUntil > now)
                return null;
            var claimed = new StockAlert
            {
                Id = current.Id, ProductId = current.ProductId, UserId = current.UserId, CreatedAt = current.CreatedAt,
                SubscribedVersion = current.SubscribedVersion, NotificationId = current.NotificationId,
                PendingProductName = name, PendingStock = stock, LeaseId = token, LeaseUntil = now + LeaseDuration
            };
            return await alerts.UpsertAsync(claimed, ct);
        }
        finally { MemoryLock.Release(); }
    }

    private async Task CompleteAsync(string id, string token, bool sent, CancellationToken ct)
    {
        if (database is not null)
        {
            var collection = database.GetCollection<StockAlert>(ServiceDefaults.CollectionNameFor(typeof(StockAlert)));
            var filter = Builders<StockAlert>.Filter.Eq(a => a.Id, id) & Builders<StockAlert>.Filter.Eq(a => a.LeaseId, token);
            if (sent) await collection.DeleteOneAsync(filter, ct);
            else await collection.UpdateOneAsync(filter, Builders<StockAlert>.Update.Set(a => a.LeaseId, null).Set(a => a.LeaseUntil, null), cancellationToken: ct);
            return;
        }

        await MemoryLock.WaitAsync(ct);
        try
        {
            var current = await alerts.GetAsync(id, ct);
            if (current?.LeaseId != token) return;
            if (sent) await alerts.DeleteAsync(id, ct);
            else
            {
                current.LeaseId = null;
                current.LeaseUntil = null;
                await alerts.UpsertAsync(current, ct);
            }
        }
        finally { MemoryLock.Release(); }
    }
}

public sealed class BackInStockRetryService(BackInStockNotifier notifier, ILogger<BackInStockRetryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try { await notifier.ProcessAsync(null, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Could not scan back-in-stock alerts."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
