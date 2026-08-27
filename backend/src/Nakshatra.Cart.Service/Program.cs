using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient<CatalogClient>();
builder.Services.AddHttpClient<RecommendationClient>();

var app = builder.Build();
app.UseNakshatraDefaults("cart-service");

app.MapGet("/api/cart/{userId}", async (string userId, IDocumentRepository<Cart> repo) =>
    Results.Ok(await repo.GetAsync(userId) ?? new Cart { Id = userId, UserId = userId }));

// Adding an item returns the updated cart together with "also purchased" recommendations.
app.MapPost("/api/cart/{userId}/items", async (
    string userId,
    AddCartItemRequest request,
    IDocumentRepository<Cart> repo,
    CatalogClient catalog,
    RecommendationClient recommendations,
    IEventPublisher publisher,
    CancellationToken ct) =>
{
    if (request.Quantity <= 0)
    {
        return Results.BadRequest(new { error = "Quantity must be greater than zero." });
    }

    var product = await catalog.GetProductAsync(request.ProductId, ct);
    if (product is null)
    {
        return Results.NotFound(new { error = $"Product '{request.ProductId}' was not found." });
    }

    var cart = await repo.GetAsync(userId, ct) ?? new Cart { Id = userId, UserId = userId };
    var existing = cart.Items.FirstOrDefault(i => i.ProductId == product.Id);
    if (existing is null)
    {
        cart.Items.Add(new CartItem { ProductId = product.Id, Name = product.Name, Price = product.Price, Quantity = request.Quantity });
    }
    else
    {
        existing.Quantity += request.Quantity;
        existing.Price = product.Price;
    }

    await repo.UpsertAsync(cart, ct);
    await publisher.PublishAsync(Topics.CartItemAdded, userId, new { userId, productId = product.Id, request.Quantity }, ct);

    var alsoPurchased = (await recommendations.GetAlsoPurchasedAsync(product.Id, 4, ct))
        .Where(p => cart.Items.All(i => i.ProductId != p.Id))
        .ToList();

    return Results.Ok(new { cart, recommendations = alsoPurchased });
});

app.MapPut("/api/cart/{userId}/items/{productId}", async (
    string userId,
    string productId,
    AddCartItemRequest request,
    IDocumentRepository<Cart> repo,
    CancellationToken ct) =>
{
    var cart = await repo.GetAsync(userId, ct);
    var item = cart?.Items.FirstOrDefault(i => i.ProductId == productId);
    if (cart is null || item is null)
    {
        return Results.NotFound();
    }

    if (request.Quantity <= 0)
    {
        cart.Items.Remove(item);
    }
    else
    {
        item.Quantity = request.Quantity;
    }

    await repo.UpsertAsync(cart, ct);
    return Results.Ok(cart);
});

app.MapDelete("/api/cart/{userId}/items/{productId}", async (
    string userId,
    string productId,
    IDocumentRepository<Cart> repo,
    CancellationToken ct) =>
{
    var cart = await repo.GetAsync(userId, ct);
    if (cart is null)
    {
        return Results.NotFound();
    }

    cart.Items.RemoveAll(i => i.ProductId == productId);
    await repo.UpsertAsync(cart, ct);
    return Results.Ok(cart);
});

app.MapDelete("/api/cart/{userId}", async (string userId, IDocumentRepository<Cart> repo, CancellationToken ct) =>
{
    var cart = new Cart { Id = userId, UserId = userId };
    await repo.UpsertAsync(cart, ct);
    return Results.Ok(cart);
});

app.Run();

public record AddCartItemRequest(string ProductId, int Quantity);

public partial class Program;
