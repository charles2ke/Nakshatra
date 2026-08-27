using Nakshatra.Recommendation.Service;
using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient<CatalogClient>();
builder.Services.AddHostedService<OrderStreamConsumer>();

var app = builder.Build();
app.UseNakshatraDefaults("recommendation-service");

// "Customers who bought this also purchased ..." – co-purchase counts first, then a
// same-category fallback so a cold graph still returns useful suggestions.
app.MapGet("/api/recommendations/also-purchased/{productId}", async (
    string productId,
    int? limit,
    IDocumentRepository<CoPurchaseGraph> repo,
    CatalogClient catalog,
    CancellationToken ct) =>
{
    var take = Math.Clamp(limit ?? 4, 1, 20);
    var products = await catalog.GetProductsAsync(ct);
    var byId = products.ToDictionary(p => p.Id);

    var graph = await repo.GetAsync(productId, ct);
    var ranked = graph is null
        ? new List<Product>()
        : graph.Counts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => byId.TryGetValue(kv.Key, out var product) ? product : null)
            .Where(p => p is not null)
            .Select(p => p!)
            .Take(take)
            .ToList();

    if (ranked.Count < take && byId.TryGetValue(productId, out var seed))
    {
        var fallback = products
            .Where(p => p.Id != productId && !ranked.Any(r => r.Id == p.Id))
            .OrderByDescending(p => p.Category == seed.Category)
            .ThenByDescending(p => p.Tags.Intersect(seed.Tags, StringComparer.OrdinalIgnoreCase).Count())
            .ThenBy(p => p.Name)
            .Take(take - ranked.Count);
        ranked.AddRange(fallback);
    }

    return Results.Ok(ranked);
});

// Lets the cart service replay a basket so co-purchase data improves before checkout.
app.MapPost("/api/recommendations/basket", async (
    BasketSignal signal,
    IDocumentRepository<CoPurchaseGraph> repo,
    CancellationToken ct) =>
{
    var productIds = signal.ProductIds.Distinct().ToList();
    if (productIds.Count > 1)
    {
        await CoPurchaseUpdater.ApplyOrderAsync(repo, productIds, ct);
    }

    return Results.Accepted();
});

await SeedCoPurchaseAsync(app.Services);
app.Run();

// Bootstraps the graph with a few historical baskets so demo recommendations are meaningful.
static async Task SeedCoPurchaseAsync(IServiceProvider services)
{
    var repo = services.GetRequiredService<IDocumentRepository<CoPurchaseGraph>>();
    if ((await repo.ListAsync()).Count > 0)
    {
        return;
    }

    var historicalBaskets = new[]
    {
        new[] { "prod-headphones", "prod-case" },
        new[] { "prod-headphones", "prod-earbuds" },
        new[] { "prod-headphones", "prod-case", "prod-mug" },
        new[] { "prod-lamp", "prod-chair" },
        new[] { "prod-chair", "prod-mug" }
    };

    foreach (var basket in historicalBaskets)
    {
        await CoPurchaseUpdater.ApplyOrderAsync(repo, basket);
    }
}

public record BasketSignal(List<string> ProductIds);

public partial class Program;
