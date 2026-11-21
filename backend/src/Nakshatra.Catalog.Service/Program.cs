using Nakshatra.Catalog.Service;
using Nakshatra.Shared;
using Nakshatra.Shared.Caching;
using Nakshatra.Shared.Endpoints;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient<OrderClient>();
builder.Services.AddScoped<BackInStockNotifier>();
builder.Services.AddSingleton<HelpfulVotes>();

var app = builder.Build();
app.UseNakshatraDefaults("catalog-service");

// Product master data, read-through cached in Redis. Ratings are earned through reviews and the
// creation date is fixed, so clients cannot set either.
app.MapCachedCrud<Product>("/api/products", "product", CatalogRules.ProductTtl,
    onCreate: product =>
    {
        product.AverageRating = 0;
        product.ReviewCount = 0;
        product.CreatedAt = DateTime.UtcNow;
    },
    onUpdate: (existing, incoming) =>
    {
        incoming.AverageRating = existing.AverageRating;
        incoming.ReviewCount = existing.ReviewCount;
        incoming.CreatedAt = existing.CreatedAt;
    },
    afterUpdate: (previous, saved, services, _) =>
        services.GetRequiredService<BackInStockNotifier>().NotifyIfRestockedAsync(previous.Stock, saved));

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
    await cache.SetAsync($"product:{id}", product, CatalogRules.ProductTtl);
    return Results.Ok(product);
});

// Stock is returned when an order is cancelled, returned or could not be placed.
app.MapPost("/api/products/{id}/release", async (
    string id,
    ReserveStockRequest request,
    IDocumentRepository<Product> repo,
    ICacheService cache,
    BackInStockNotifier backInStock) =>
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

    var previousStock = product.Stock;
    product.Stock += request.Quantity;
    await repo.UpsertAsync(product);
    await cache.RemoveAsync("product:all");
    await cache.SetAsync($"product:{id}", product, CatalogRules.ProductTtl);
    await backInStock.NotifyIfRestockedAsync(previousStock, product);
    return Results.Ok(product);
});

// Product reviews. Review photos and videos are uploaded to the media service, which transcodes
// them asynchronously; only the resulting media ids are stored here.
app.MapGet("/api/products/{productId}/reviews", async (string productId, IDocumentRepository<Review> repo, CancellationToken ct) =>
    Results.Ok((await repo.FindAsync(r => r.ProductId == productId, ct)).OrderByDescending(r => r.CreatedAt).ToList()));

app.MapPost("/api/products/{productId}/reviews", async (
    string productId,
    Review review,
    IDocumentRepository<Product> products,
    IDocumentRepository<Review> repo,
    ICacheService cache,
    OrderClient orders,
    CancellationToken ct) =>
{
    if (await products.GetAsync(productId, ct) is null)
    {
        return Results.NotFound(new { error = $"Product '{productId}' was not found." });
    }

    if (review.Rating is < 1 or > 5)
    {
        return Results.BadRequest(new { error = "Rating must be between 1 and 5." });
    }

    review.Id = Guid.NewGuid().ToString("N");
    review.ProductId = productId;
    review.CreatedAt = DateTime.UtcNow;
    // Verification and helpful votes are decided by the server, never taken from the request.
    review.VerifiedPurchase = await CatalogRules.IsVerifiedPurchaseAsync(orders, review.UserId, productId, ct);
    review.HelpfulVoterIds = new List<string>();
    review.HelpfulCount = 0;
    await repo.UpsertAsync(review, ct);
    await cache.RemoveAsync($"reviews:{productId}");
    await CatalogRules.RefreshRatingAsync(productId, products, repo, cache, ct);
    return Results.Created($"/api/products/{productId}/reviews/{review.Id}", review);
});

// "Was this review helpful?" Each shopper counts once, and authors cannot vote on their own review.
app.MapPost("/api/products/{productId}/reviews/{reviewId}/helpful", async (
    string productId,
    string reviewId,
    HelpfulVoteRequest request,
    HelpfulVotes votes,
    CancellationToken ct) =>
{
    var userId = request.UserId?.Trim();
    if (string.IsNullOrEmpty(userId))
    {
        return Results.BadRequest(new { error = "A user id is required to vote." });
    }

    var review = await votes.RecordAsync(productId, reviewId, userId, ct);
    if (review is null || review.ProductId != productId)
    {
        return Results.NotFound(new { error = $"Review '{reviewId}' was not found." });
    }

    if (review.UserId == userId)
    {
        return Results.BadRequest(new { error = "You cannot vote on your own review." });
    }

    return Results.Ok(review);
});

// Customer questions and answers.
app.MapGet("/api/products/{productId}/questions", async (string productId, IDocumentRepository<ProductQuestion> repo, CancellationToken ct) =>
    Results.Ok((await repo.FindAsync(q => q.ProductId == productId, ct)).OrderByDescending(q => q.CreatedAt).ToList()));

app.MapPost("/api/products/{productId}/questions", async (
    string productId,
    AskQuestionRequest request,
    IDocumentRepository<Product> products,
    IDocumentRepository<ProductQuestion> repo,
    CancellationToken ct) =>
{
    if (ValidatePost(request.UserId, request.Text) is { } invalid)
    {
        return invalid;
    }

    if (await products.GetAsync(productId, ct) is null)
    {
        return Results.NotFound(new { error = $"Product '{productId}' was not found." });
    }

    var question = new ProductQuestion
    {
        ProductId = productId,
        UserId = request.UserId!.Trim(),
        UserName = request.UserName?.Trim() ?? string.Empty,
        Text = request.Text!.Trim()
    };
    await repo.UpsertAsync(question, ct);
    return Results.Created($"/api/products/{productId}/questions/{question.Id}", question);
});

app.MapPost("/api/products/{productId}/questions/{questionId}/answers", async (
    string productId,
    string questionId,
    AnswerQuestionRequest request,
    IDocumentRepository<ProductQuestion> repo,
    CancellationToken ct) =>
{
    if (ValidatePost(request.UserId, request.Text) is { } invalid)
    {
        return invalid;
    }

    var question = await repo.GetAsync(questionId, ct);
    if (question is null || question.ProductId != productId)
    {
        return Results.NotFound(new { error = $"Question '{questionId}' was not found." });
    }

    var answer = new ProductAnswer
    {
        UserId = request.UserId!.Trim(),
        UserName = request.UserName?.Trim() ?? string.Empty,
        Text = request.Text!.Trim(),
        // The portal has no sign-in, so like the rest of the demo the persona is taken from the client.
        IsSeller = string.Equals(request.Persona, nameof(Persona.Vendor), StringComparison.OrdinalIgnoreCase)
    };
    question.Answers.Add(answer);
    await repo.UpsertAsync(question, ct);
    return Results.Created($"/api/products/{productId}/questions/{questionId}/answers/{answer.Id}", question);
});

// "Notify me when available" for out-of-stock products.
app.MapPost("/api/products/{productId}/stock-alerts", async (
    string productId,
    StockAlertRequest request,
    IDocumentRepository<Product> products,
    IDocumentRepository<StockAlert> alerts,
    CancellationToken ct) =>
{
    var userId = request.UserId?.Trim();
    if (string.IsNullOrEmpty(userId))
    {
        return Results.BadRequest(new { error = "A user id is required." });
    }

    var product = await products.GetAsync(productId, ct);
    if (product is null)
    {
        return Results.NotFound(new { error = $"Product '{productId}' was not found." });
    }

    if (product.Stock > 0)
    {
        return Results.Conflict(new { error = "The product is in stock.", available = product.Stock });
    }

    var alertId = StockAlert.IdFor(productId, userId);
    var alert = await alerts.GetAsync(alertId, ct);
    if (alert is null)
    {
        alert = new StockAlert { Id = alertId, ProductId = productId, UserId = userId };
        await alerts.UpsertAsync(alert, ct);
    }

    return Results.Ok(alert);
});

app.MapGet("/api/products/{productId}/stock-alerts/{userId}", async (
    string productId,
    string userId,
    IDocumentRepository<StockAlert> alerts,
    CancellationToken ct) =>
    Results.Ok(new { subscribed = await alerts.GetAsync(StockAlert.IdFor(productId, userId), ct) is not null }));

app.MapDelete("/api/products/{productId}/stock-alerts/{userId}", async (
    string productId,
    string userId,
    IDocumentRepository<StockAlert> alerts,
    CancellationToken ct) =>
{
    await alerts.DeleteAsync(StockAlert.IdFor(productId, userId), ct);
    return Results.NoContent();
});

await SeedData.SeedAsync(app.Services, SeedData.Products);
await SeedData.SeedAsync(app.Services, SeedData.Reviews);
app.Run();

static IResult? ValidatePost(string? userId, string? text)
{
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.BadRequest(new { error = "A user id is required." });
    }

    var length = text?.Trim().Length ?? 0;
    return length is 0 or > CatalogRules.MaxQuestionLength
        ? Results.BadRequest(new { error = $"Text must be between 1 and {CatalogRules.MaxQuestionLength} characters." })
        : null;
}

public record ReserveStockRequest(int Quantity);

public partial class Program;
