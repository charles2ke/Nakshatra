using Nakshatra.Shared;
using Nakshatra.Shared.Caching;
using Nakshatra.Shared.Endpoints;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseNakshatraDefaults("catalog-service");

// Product master data, read-through cached in Redis.
app.MapCachedCrud<Product>("/api/products", "product", TimeSpan.FromMinutes(10));

app.MapGet("/api/products/by-vendor/{vendorId}", async (string vendorId, IDocumentRepository<Product> repo) =>
    Results.Ok(await repo.FindAsync(p => p.VendorId == vendorId)));

app.MapGet("/api/products/categories", async (IDocumentRepository<Product> repo, ICacheService cache) =>
{
    var categories = await cache.GetOrSetAsync("product:categories", async () =>
        (await repo.ListAsync())
            .Select(p => p.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList(),
        TimeSpan.FromMinutes(5));

    return Results.Ok(categories);
});

// Stock is decremented when an order is placed.
app.MapPost("/api/products/{id}/reserve", async (string id, ReserveStockRequest request, IDocumentRepository<Product> repo, ICacheService cache) =>
{
    if (request.Quantity <= 0)
    {
        return Results.BadRequest(new { error = "Quantity must be greater than zero." });
    }

    var product = await repo.GetAsync(id);
    if (product is null)
    {
        return Results.NotFound();
    }

    if (product.Stock < request.Quantity)
    {
        return Results.Conflict(new { error = "Insufficient stock.", available = product.Stock });
    }

    product.Stock -= request.Quantity;
    await repo.UpsertAsync(product);
    await cache.RemoveAsync("product:all");
    await cache.SetAsync($"product:{id}", product, TimeSpan.FromMinutes(10));
    return Results.Ok(product);
});

await SeedData.SeedAsync(app.Services, SeedData.Products);
app.Run();

public record ReserveStockRequest(int Quantity);

public partial class Program;
