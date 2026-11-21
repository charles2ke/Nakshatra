using Nakshatra.Cart.Service;
using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient<CatalogClient>();
builder.Services.AddHttpClient<RecommendationClient>();
builder.Services.AddHostedService<OrderStreamConsumer>();

var app = builder.Build();
app.UseNakshatraDefaults("cart-service");

app.MapGet("/api/cart/{userId}", async (string userId, IDocumentRepository<Cart> repo) =>
    Results.Ok(await repo.GetAsync(userId) ?? CartLines.Empty(userId)));

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

    var (cart, _) = await ShoppingState.ChangeCartAsync(repo, userId, true, cart =>
    {
        CartLines.Add(cart.Items, product.Id, product.Name, product.Price, request.Quantity);
        return StateChange.Changed;
    }, ct);
    await publisher.PublishAsync(Topics.CartItemAdded, userId, new { userId, productId = product.Id, request.Quantity }, ct);

    var alsoPurchased = (await recommendations.GetAlsoPurchasedAsync(product.Id, 4, ct))
        .Where(p => cart!.Items.All(i => i.ProductId != p.Id))
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
    var (cart, _) = await ShoppingState.ChangeCartAsync(repo, userId, false, cart =>
    {
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item is null) return StateChange.Missing;
        if (request.Quantity <= 0) cart.Items.Remove(item);
        else item.Quantity = request.Quantity;
        return StateChange.Changed;
    }, ct);
    if (cart is null) return Results.NotFound();
    return Results.Ok(cart);
});

app.MapDelete("/api/cart/{userId}/items/{productId}", async (
    string userId,
    string productId,
    IDocumentRepository<Cart> repo,
    CancellationToken ct) =>
{
    var (cart, _) = await ShoppingState.ChangeCartAsync(repo, userId, false, cart =>
    {
        cart.Items.RemoveAll(i => i.ProductId == productId);
        return StateChange.Changed;
    }, ct);
    if (cart is null) return Results.NotFound();
    return Results.Ok(cart);
});

// Empties the cart for checkout; items saved for later stay saved.
app.MapDelete("/api/cart/{userId}", async (string userId, IDocumentRepository<Cart> repo, CancellationToken ct) =>
{
    var (cart, _) = await ShoppingState.ChangeCartAsync(repo, userId, true, cart =>
    {
        cart.Items.Clear();
        return StateChange.Changed;
    }, ct);
    return Results.Ok(cart);
});

// "Save for later" moves a line out of the cart without losing it.
app.MapPost("/api/cart/{userId}/items/{productId}/save-for-later", async (
    string userId,
    string productId,
    IDocumentRepository<Cart> repo,
    CancellationToken ct) =>
{
    var (cart, _) = await ShoppingState.ChangeCartAsync(repo, userId, false, cart =>
    {
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item is null) return StateChange.Missing;
        cart.Items.Remove(item);
        CartLines.Add(cart.SavedForLater, item.ProductId, item.Name, item.Price, item.Quantity);
        return StateChange.Changed;
    }, ct);
    if (cart is null) return Results.NotFound();
    return Results.Ok(cart);
});

app.MapPost("/api/cart/{userId}/saved/{productId}/move-to-cart", async (
    string userId,
    string productId,
    IDocumentRepository<Cart> repo,
    CatalogClient catalog,
    IEventPublisher publisher,
    CancellationToken ct) =>
{
    // The price may have changed while the item was set aside; keep the saved price if the catalog is down.
    var lookup = await catalog.LookupProductAsync(productId, ct);
    if (lookup.Status == ProductLookupStatus.NotFound)
    {
        return Results.NotFound(new { error = $"Product '{productId}' is no longer available." });
    }

    var quantity = 0;
    var (cart, _) = await ShoppingState.ChangeCartAsync(repo, userId, false, cart =>
    {
        var saved = cart.SavedForLater.FirstOrDefault(i => i.ProductId == productId);
        if (saved is null) return StateChange.Missing;
        quantity = saved.Quantity;
        cart.SavedForLater.Remove(saved);
        CartLines.Add(cart.Items, productId, lookup.Product?.Name ?? saved.Name, lookup.Product?.Price ?? saved.Price, quantity);
        return StateChange.Changed;
    }, ct);
    if (cart is null) return Results.NotFound();
    await publisher.PublishAsync(Topics.CartItemAdded, userId, new { userId, productId, Quantity = quantity }, ct);
    return Results.Ok(cart);
});

app.MapDelete("/api/cart/{userId}/saved/{productId}", async (
    string userId,
    string productId,
    IDocumentRepository<Cart> repo,
    CancellationToken ct) =>
{
    var (cart, _) = await ShoppingState.ChangeCartAsync(repo, userId, false, cart =>
    {
        cart.SavedForLater.RemoveAll(i => i.ProductId == productId);
        return StateChange.Changed;
    }, ct);
    if (cart is null) return Results.NotFound();
    return Results.Ok(cart);
});

// Wish lists. Each user has one list, keyed by user id, shown with live prices and stock.
app.MapGet("/api/wishlist/{userId}", async (string userId, IDocumentRepository<WishList> repo, CatalogClient catalog, CancellationToken ct) =>
{
    var list = await repo.GetAsync(userId, ct) ?? new WishList { Id = userId, UserId = userId };
    return Results.Ok(await CartLines.ToViewAsync(list, catalog, ct));
});

app.MapPost("/api/wishlist/{userId}/items", async (
    string userId,
    WishListItemRequest request,
    IDocumentRepository<WishList> repo,
    CatalogClient catalog,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.ProductId))
    {
        return Results.BadRequest(new { error = "A product id is required." });
    }

    var lookup = await catalog.LookupProductAsync(request.ProductId, ct);
    if (lookup.Status == ProductLookupStatus.Unavailable)
    {
        return Results.Json(new { error = "The catalog could not be reached right now. Please try again." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (lookup.Product is not { } product)
    {
        return Results.NotFound(new { error = $"Product '{request.ProductId}' was not found." });
    }

    var list = await ShoppingState.ChangeWishListAsync(repo, userId, true, list =>
    {
        if (list.Items.Any(i => i.ProductId == product.Id)) return StateChange.Unchanged;
        list.Items.Add(new WishListItem { ProductId = product.Id, Name = product.Name, PriceWhenAdded = product.Price, EntryId = Guid.NewGuid().ToString("N") });
        return StateChange.Changed;
    }, ct);

    return Results.Ok(await CartLines.ToViewAsync(list!, catalog, ct));
});

app.MapDelete("/api/wishlist/{userId}/items/{productId}", async (
    string userId,
    string productId,
    IDocumentRepository<WishList> repo,
    CatalogClient catalog,
    CancellationToken ct) =>
{
    var list = await ShoppingState.ChangeWishListAsync(repo, userId, false, list =>
    {
        list.Items.RemoveAll(i => i.ProductId == productId);
        return StateChange.Changed;
    }, ct);
    if (list is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(await CartLines.ToViewAsync(list, catalog, ct));
});

app.MapPost("/api/wishlist/{userId}/items/{productId}/move-to-cart", async (
    string userId,
    string productId,
    IDocumentRepository<WishList> wishLists,
    IDocumentRepository<Cart> carts,
    CatalogClient catalog,
    IEventPublisher publisher,
    CancellationToken ct) =>
{
    // Assign legacy entries an identity with a conditional write before creating a receipt.
    var list = await ShoppingState.ChangeWishListAsync(wishLists, userId, false, current =>
    {
        var item = current.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item is null) return StateChange.Missing;
        if (!string.IsNullOrEmpty(item.EntryId)) return StateChange.Unchanged;
        item.EntryId = Guid.NewGuid().ToString("N");
        return StateChange.Changed;
    }, ct);
    var entry = list?.Items.FirstOrDefault(i => i.ProductId == productId);
    if (entry is null) return Results.NotFound();
    var moveId = entry.EntryId;
    var cart = await carts.GetAsync(userId, ct);
    var added = false;
    if (cart is null || !cart.AppliedWishListMoves.Contains(moveId))
    {
        var lookup = await catalog.LookupProductAsync(productId, ct);
        if (lookup.Status == ProductLookupStatus.Unavailable)
            return Results.Json(new { error = "The catalog could not be reached right now. Please try again." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        if (lookup.Product is not { } product)
            return Results.NotFound(new { error = $"Product '{productId}' is no longer available." });

        (cart, added) = await ShoppingState.ChangeCartAsync(carts, userId, true, current =>
        {
            if (current.AppliedWishListMoves.Contains(moveId)) return StateChange.Unchanged;
            CartLines.Add(current.Items, product.Id, product.Name, product.Price, 1);
            current.AppliedWishListMoves.Add(moveId);
            return StateChange.Changed;
        }, ct);
    }
    list = await ShoppingState.ChangeWishListAsync(wishLists, userId, false, current =>
    {
        var removed = current.Items.RemoveAll(i => i.EntryId == moveId);
        return removed == 0 ? StateChange.Unchanged : StateChange.Changed;
    }, ct) ?? new WishList { Id = userId, UserId = userId };
    if (added)
        await publisher.PublishAsync(Topics.CartItemAdded, userId, new { userId, productId, Quantity = 1 }, ct);

    return Results.Ok(new { wishList = await CartLines.ToViewAsync(list, catalog, ct), cart });
});

app.Run();

public record AddCartItemRequest(string ProductId, int Quantity);

public partial class Program;
