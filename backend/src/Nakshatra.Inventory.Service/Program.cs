using Nakshatra.Inventory.Service;
using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient<CatalogClient>();
builder.Services.AddHostedService<OrderStreamConsumer>();

var app = builder.Build();
app.UseNakshatraDefaults("inventory-service");

app.MapGet("/api/inventory", async (string? vendorId, IDocumentRepository<InventoryItem> repo, CancellationToken ct) =>
{
    var items = await repo.ListAsync(ct);
    var filtered = string.IsNullOrWhiteSpace(vendorId)
        ? items
        : items.Where(i => i.VendorId == vendorId).ToList();

    return Results.Ok(filtered.OrderBy(i => i.ProductId).ToList());
});

app.MapGet("/api/inventory/{productId}", async (string productId, IDocumentRepository<InventoryItem> repo, CancellationToken ct) =>
    await repo.GetAsync(productId, ct) is { } item ? Results.Ok(item) : Results.NotFound());

// Goods received, cycle counts and write-offs.
app.MapPost("/api/inventory/{productId}/adjust", async (
    string productId,
    StockAdjustment adjustment,
    IDocumentRepository<InventoryItem> repo,
    IEventPublisher publisher,
    CancellationToken ct) =>
{
    var item = await repo.GetAsync(productId, ct) ?? new InventoryItem { Id = productId, ProductId = productId };

    if (item.OnHand + adjustment.Delta < 0)
    {
        return Results.Conflict(new { error = "Adjustment would drive stock below zero.", onHand = item.OnHand });
    }

    item.OnHand += adjustment.Delta;
    if (adjustment.Delta > 0)
    {
        item.OnOrder = Math.Max(item.OnOrder - adjustment.Delta, 0);
    }

    item.UpdatedAt = DateTime.UtcNow;
    await repo.UpsertAsync(item, ct);

    await publisher.PublishAsync(Topics.InventoryAdjusted, productId, new
    {
        productId,
        adjustment.Delta,
        reason = adjustment.Reason ?? string.Empty,
        onHand = item.OnHand
    }, ct);

    return Results.Ok(item);
});

// Registers a replenishment purchase order so the stock position includes goods in transit.
app.MapPost("/api/inventory/{productId}/replenish", async (
    string productId,
    ReplenishmentRequest request,
    IDocumentRepository<InventoryItem> repo,
    IEventPublisher publisher,
    CancellationToken ct) =>
{
    if (request.Quantity <= 0)
    {
        return Results.BadRequest(new { error = "Quantity must be greater than zero." });
    }

    var item = await repo.GetAsync(productId, ct) ?? new InventoryItem { Id = productId, ProductId = productId };
    item.OnOrder += request.Quantity;
    item.UpdatedAt = DateTime.UtcNow;
    await repo.UpsertAsync(item, ct);

    await publisher.PublishAsync(Topics.InventoryReplenishmentOrdered, productId, new
    {
        productId,
        request.Quantity,
        vendorId = item.VendorId,
        expectedArrival = DateTime.UtcNow.AddDays(item.LeadTimeDays)
    }, ct);

    return Results.Ok(item);
});

// Demand forecast and the resulting optimal stock policy for one product.
app.MapGet("/api/inventory/{productId}/forecast", async (
    string productId,
    int? horizonDays,
    double? serviceLevelZ,
    IDocumentRepository<InventoryItem> repo,
    CancellationToken ct) =>
{
    var item = await repo.GetAsync(productId, ct);
    return item is null
        ? Results.NotFound()
        : Results.Ok(DemandForecaster.Forecast(item, horizonDays ?? 30, serviceLevelZ ?? 1.65));
});

// Everything that should be reordered now, most urgent first.
app.MapGet("/api/inventory/reorder-suggestions", async (
    int? horizonDays,
    IDocumentRepository<InventoryItem> repo,
    CancellationToken ct) =>
{
    var items = await repo.ListAsync(ct);
    var suggestions = items
        .Select(item => DemandForecaster.Forecast(item, horizonDays ?? 30))
        .Where(f => f.ReorderNow)
        .OrderBy(f => f.DaysOfCoverRemaining)
        .ToList();

    return Results.Ok(suggestions);
});

// Historical demand used by the forecast, for the inventory dashboard chart.
app.MapGet("/api/inventory/{productId}/sales-history", async (
    string productId,
    int? days,
    IDocumentRepository<InventoryItem> repo,
    CancellationToken ct) =>
{
    var item = await repo.GetAsync(productId, ct);
    if (item is null)
    {
        return Results.NotFound();
    }

    var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-(days ?? 90)));
    return Results.Ok(item.SalesHistory.Where(p => p.Date >= cutoff).OrderBy(p => p.Date).ToList());
});

await InventorySeed.EnsureSeededAsync(app.Services);
app.Run();

public record StockAdjustment(int Delta, string? Reason);

public record ReplenishmentRequest(int Quantity);
