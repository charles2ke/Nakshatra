using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Nakshatra.Catalog.Service;
using Nakshatra.Shared;
using Nakshatra.Shared.Caching;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;
using CatalogService = Nakshatra.Catalog.Service.ServiceMarker;
using OrderEntity = Nakshatra.Shared.Models.Order;
using SearchService = Nakshatra.Search.Service.ServiceMarker;

namespace Nakshatra.Tests;

/// <summary>Product discovery: ratings, verified reviews, helpful votes, Q&amp;A, stock alerts and search.</summary>
public class DiscoveryTests
{
    [Fact]
    public async Task Reviews_from_buyers_are_verified_and_ratings_are_earned_not_set()
    {
        var buyerId = $"user-{Guid.NewGuid():N}";
        var productId = string.Empty;
        using var catalog = TestHosts.Create<CatalogService>(services =>
            services.Route<OrderClient>(() => new LambdaHttpMessageHandler(request =>
                TestHosts.Json(request.RequestUri!.AbsolutePath.EndsWith(buyerId, StringComparison.Ordinal)
                    ? new[] { new OrderEntity { UserId = buyerId, Status = OrderStatus.Delivered, Items = { new CartItem { ProductId = productId } } } }
                    : Array.Empty<OrderEntity>()))));
        using var client = catalog.CreateClient();
        var product = await CreateProductAsync(client, stock: 3, averageRating: 5, reviewCount: 40);
        productId = product.Id;

        var fromBuyer = await PostReviewAsync(client, productId, buyerId, rating: 4);
        var fromBrowser = await PostReviewAsync(client, productId, $"user-{Guid.NewGuid():N}", rating: 1, claimVerified: true);
        product.Name = "Renamed";
        product.AverageRating = 5;
        (await client.PutAsJsonAsync($"/api/products/{productId}", product, ServiceDefaults.JsonOptions)).EnsureSuccessStatusCode();

        Assert.True(fromBuyer.VerifiedPurchase);
        Assert.False(fromBrowser.VerifiedPurchase);
        Assert.Equal(0, fromBrowser.HelpfulCount);
        var stored = await client.GetFromJsonAsync<Product>($"/api/products/{productId}", ServiceDefaults.JsonOptions);
        Assert.Equal("Renamed", stored!.Name);
        Assert.Equal(2.5, stored.AverageRating);
        Assert.Equal(2, stored.ReviewCount);
    }

    public sealed class MongoFactAttribute : FactAttribute
    {
        public MongoFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NAKSHATRA_TEST_MONGO")))
            {
                Skip = "Set NAKSHATRA_TEST_MONGO to run MongoDB integration tests.";
            }
        }
    }

    [Fact]
    public async Task Helpful_votes_count_each_shopper_once_and_never_the_author()
    {
        using var catalog = TestHosts.Create<CatalogService>(NoOrderService);
        using var client = catalog.CreateClient();
        var product = await CreateProductAsync(client, stock: 3);
        var authorId = $"user-{Guid.NewGuid():N}";
        var review = await PostReviewAsync(client, product.Id, authorId, rating: 5);
        var path = $"/api/products/{product.Id}/reviews/{review.Id}/helpful";

        await client.PostAsJsonAsync(path, new { userId = "voter-1" });
        var repeated = await client.PostAsJsonAsync(path, new { userId = "voter-1" });
        var second = await client.PostAsJsonAsync(path, new { userId = "voter-2" });
        var byAuthor = await client.PostAsJsonAsync(path, new { userId = authorId });
        var wrongProduct = await client.PostAsJsonAsync($"/api/products/prod-lamp/reviews/{review.Id}/helpful", new { userId = "voter-3" });

        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(2, (await second.Content.ReadFromJsonAsync<Review>(ServiceDefaults.JsonOptions))!.HelpfulCount);
        Assert.Equal(HttpStatusCode.BadRequest, byAuthor.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongProduct.StatusCode);
        // Voter ids are kept private.
        Assert.DoesNotContain("voter-1", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_helpful_votes_retain_distinct_shoppers_and_ignore_duplicates()
    {
        using var catalog = TestHosts.Create<CatalogService>(NoOrderService);
        await AssertConcurrentHelpfulVotesAsync(catalog);
    }

    [MongoFact]
    public async Task Mongo_concurrent_helpful_votes_retain_distinct_shoppers_and_ignore_duplicates()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("NAKSHATRA_TEST_MONGO"));
        var databaseName = $"nakshatra_votes_{Guid.NewGuid():N}";
        var database = client.GetDatabase(databaseName);
        try
        {
            using var catalog = TestHosts.Create<CatalogService>(services =>
            {
                NoOrderService(services);
                services.AddSingleton(database);
                services.AddSingleton<IDocumentRepository<Review>>(
                    new MongoDocumentRepository<Review>(database, ServiceDefaults.CollectionNameFor(typeof(Review))));
            });
            await AssertConcurrentHelpfulVotesAsync(catalog);
        }
        finally
        {
            await client.DropDatabaseAsync(databaseName);
        }
    }

    private static async Task AssertConcurrentHelpfulVotesAsync(WebApplicationFactory<CatalogService> catalog)
    {
        using var client = catalog.CreateClient();
        var product = await CreateProductAsync(client, stock: 3);
        var review = await PostReviewAsync(client, product.Id, $"author-{Guid.NewGuid():N}", rating: 5);
        var path = $"/api/products/{product.Id}/reviews/{review.Id}/helpful";
        var voters = Enumerable.Range(0, 40).Select(i => $"voter-{i}").ToArray();

        var responses = await Task.WhenAll(voters.Concat(voters).Select(userId =>
            Task.Run(() => client.PostAsJsonAsync(path, new { userId }))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var stored = await catalog.Services.GetRequiredService<IDocumentRepository<Review>>().GetAsync(review.Id);
        Assert.NotNull(stored);
        Assert.Equal(voters.Length, stored.HelpfulCount);
        Assert.Equal(voters.OrderBy(x => x), stored.HelpfulVoterIds.OrderBy(x => x));
    }

    [Fact]
    public async Task Questions_are_answered_by_shoppers_and_sellers()
    {
        using var catalog = TestHosts.Create<CatalogService>(NoOrderService);
        using var client = catalog.CreateClient();
        var product = await CreateProductAsync(client, stock: 3);
        var path = $"/api/products/{product.Id}/questions";

        var asked = await client.PostAsJsonAsync(path, new { userId = "user-customer", userName = "Asha", text = "  Does it fold?  " });
        var question = await asked.Content.ReadFromJsonAsync<ProductQuestion>(ServiceDefaults.JsonOptions);
        await client.PostAsJsonAsync($"{path}/{question!.Id}/answers", new { userId = "user-vendor", userName = "Aurora", text = "Yes, flat.", persona = "Vendor" });
        await client.PostAsJsonAsync($"{path}/{question.Id}/answers", new { userId = "user-other", text = "Mine does.", persona = "Customer" });
        var blank = await client.PostAsJsonAsync(path, new { userId = "user-customer", text = " " });
        var tooLong = await client.PostAsJsonAsync(path, new { userId = "user-customer", text = new string('a', CatalogRules.MaxQuestionLength + 1) });
        var unknownProduct = await client.PostAsJsonAsync("/api/products/does-not-exist/questions", new { userId = "user-customer", text = "Hello?" });

        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownProduct.StatusCode);
        var listed = Assert.Single((await client.GetFromJsonAsync<List<ProductQuestion>>(path, ServiceDefaults.JsonOptions))!);
        Assert.Equal("Does it fold?", listed.Text);
        Assert.Equal(new[] { true, false }, listed.Answers.Select(a => a.IsSeller));
    }

    [Fact]
    public async Task Stock_alerts_notify_each_subscriber_once_when_stock_returns()
    {
        var events = new CapturingPublisher();
        using var catalog = TestHosts.Create<CatalogService>(services =>
        {
            NoOrderService(services);
            services.AddSingleton<IEventPublisher>(events);
        });
        using var client = catalog.CreateClient();
        var product = await CreateProductAsync(client, stock: 0);
        var inStock = await CreateProductAsync(client, stock: 2);
        var alerts = $"/api/products/{product.Id}/stock-alerts";

        await client.PostAsJsonAsync(alerts, new { userId = "shopper-a" });
        await client.PostAsJsonAsync(alerts, new { userId = "shopper-a" });
        await client.PostAsJsonAsync(alerts, new { userId = "shopper-b" });
        var alreadyAvailable = await client.PostAsJsonAsync($"/api/products/{inStock.Id}/stock-alerts", new { userId = "shopper-a" });
        var subscribed = await client.GetFromJsonAsync<JsonElement>($"{alerts}/shopper-a");

        product.Stock = 4;
        (await client.PutAsJsonAsync($"/api/products/{product.Id}", product, ServiceDefaults.JsonOptions)).EnsureSuccessStatusCode();
        product.Stock = 6;
        (await client.PutAsJsonAsync($"/api/products/{product.Id}", product, ServiceDefaults.JsonOptions)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, alreadyAvailable.StatusCode);
        Assert.True(subscribed.GetProperty("subscribed").GetBoolean());
        var notified = events.Events.Where(e => e.Topic == Topics.ProductsBackInStock).ToList();
        Assert.Equal(new[] { "shopper-a", "shopper-b" }, notified.Select(e => e.Payload.GetProperty("userId").GetString()).Order());
        Assert.All(notified, e => Assert.Equal(product.Name, e.Payload.GetProperty("productName").GetString()));
        Assert.False((await client.GetFromJsonAsync<JsonElement>($"{alerts}/shopper-a")).GetProperty("subscribed").GetBoolean());
    }

    [Fact]
    public async Task Stock_released_by_a_cancelled_order_triggers_back_in_stock_alerts()
    {
        var events = new CapturingPublisher();
        using var catalog = TestHosts.Create<CatalogService>(services =>
        {
            NoOrderService(services);
            services.AddSingleton<IEventPublisher>(events);
        });
        using var client = catalog.CreateClient();
        var product = await CreateProductAsync(client, stock: 0);
        await client.PostAsJsonAsync($"/api/products/{product.Id}/stock-alerts", new { userId = "shopper-c" });

        (await client.PostAsJsonAsync($"/api/products/{product.Id}/release", new { quantity = 1 })).EnsureSuccessStatusCode();

        var notified = Assert.Single(events.Events, e => e.Topic == Topics.ProductsBackInStock);
        Assert.Equal(product.Id, notified.Key);
        Assert.Equal(1, notified.Payload.GetProperty("stock").GetInt32());
    }

    [Theory]
    [InlineData("sort=price-asc", "budget,deal,premium")]
    [InlineData("sort=price-desc", "premium,deal,budget")]
    [InlineData("sort=rating", "deal,budget,premium")]
    [InlineData("sort=newest", "deal,premium,budget")]
    [InlineData("minRating=4&sort=price-asc", "budget,deal")]
    [InlineData("inStock=true&sort=price-asc", "deal,premium")]
    [InlineData("deals=true", "deal")]
    [InlineData("q=lamp&inStock=true", "premium")]
    public async Task Search_filters_by_rating_availability_and_deals_and_sorts(string query, string expected)
    {
        using var search = SearchServiceOver(
            new Product { Id = "budget", Name = "Budget lamp", Price = 10, AverageRating = 4.2, Stock = 0, CreatedAt = new DateTime(2024, 1, 1) },
            new Product { Id = "deal", Name = "Deal kettle", Price = 20, ListPrice = 30, AverageRating = 4.8, Stock = 2, CreatedAt = new DateTime(2024, 3, 1) },
            new Product { Id = "premium", Name = "Premium lamp", Price = 40, ListPrice = 40, AverageRating = 3.1, Stock = 9, CreatedAt = new DateTime(2024, 2, 1) });
        using var client = search.CreateClient();

        var result = await client.GetFromJsonAsync<JsonElement>($"/api/search?{query}");

        var ids = result.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString());
        Assert.Equal(expected, string.Join(',', ids));
    }

    [Theory]
    [InlineData("sort=cheapest")]
    [InlineData("minRating=6")]
    public async Task Search_rejects_unknown_sorts_and_out_of_range_ratings(string query)
    {
        using var search = SearchServiceOver();
        using var client = search.CreateClient();

        var response = await client.GetAsync($"/api/search?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Verified-purchase lookups find no orders instead of calling a real order service.
    private static void NoOrderService(IServiceCollection services)
        => services.Route<OrderClient>(() => new StubHttpMessageHandler(HttpStatusCode.OK, "[]"));

    private static WebApplicationFactory<SearchService> SearchServiceOver(params Product[] products)
        => TestHosts.Create<SearchService>(services =>
        {
            services.AddSingleton<ICacheService, PassThroughCache>();
            services.Route<CatalogClient>(() => new LambdaHttpMessageHandler(_ => TestHosts.Json(products)));
        });

    private static async Task<Product> CreateProductAsync(HttpClient catalog, int stock, double averageRating = 0, int reviewCount = 0)
    {
        var response = await catalog.PostAsJsonAsync("/api/products", new Product
        {
            Name = $"Discovery {Guid.NewGuid():N}",
            Category = "Home",
            Price = 25m,
            Stock = stock,
            AverageRating = averageRating,
            ReviewCount = reviewCount,
            VendorId = SeedData.VendorAuroraId
        }, ServiceDefaults.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Product>(ServiceDefaults.JsonOptions))!;
    }

    private static async Task<Review> PostReviewAsync(HttpClient catalog, string productId, string userId, int rating, bool claimVerified = false)
    {
        var response = await catalog.PostAsJsonAsync($"/api/products/{productId}/reviews", new
        {
            userId,
            title = "Review",
            body = "Thoughts.",
            rating,
            verifiedPurchase = claimVerified,
            helpfulCount = claimVerified ? 99 : 0
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Review>(ServiceDefaults.JsonOptions))!;
    }
}
