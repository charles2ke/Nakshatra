using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task Concurrent_answers_are_all_preserved()
    {
        using var catalog = TestHosts.Create<CatalogService>(NoOrderService);
        await AssertConcurrentAnswersAsync(catalog);
    }

    [MongoFact]
    public async Task Mongo_concurrent_answers_are_all_preserved()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("NAKSHATRA_TEST_MONGO"));
        var name = $"nakshatra_answers_{Guid.NewGuid():N}";
        var database = client.GetDatabase(name);
        try
        {
            using var catalog = TestHosts.Create<CatalogService>(services =>
            {
                NoOrderService(services);
                services.AddSingleton(database);
                services.AddSingleton<IDocumentRepository<ProductQuestion>>(new MongoDocumentRepository<ProductQuestion>(database, ServiceDefaults.CollectionNameFor(typeof(ProductQuestion))));
            });
            await AssertConcurrentAnswersAsync(catalog);
        }
        finally { await client.DropDatabaseAsync(name); }
    }

    private static async Task AssertConcurrentAnswersAsync(WebApplicationFactory<CatalogService> catalog)
    {
        using var client = catalog.CreateClient();
        var product = await CreateProductAsync(client, 1);
        var path = $"/api/products/{product.Id}/questions";
        var response = await client.PostAsJsonAsync(path, new { userId = "asker", text = "Question?" });
        var question = (await response.Content.ReadFromJsonAsync<ProductQuestion>(ServiceDefaults.JsonOptions))!;
        var users = Enumerable.Range(0, 40).Select(i => $"answerer-{i}").ToArray();
        var results = await Task.WhenAll(users.Select(userId => Task.Run(() =>
            client.PostAsJsonAsync($"{path}/{question.Id}/answers", new { userId, text = userId }))));
        Assert.All(results, result => Assert.Equal(HttpStatusCode.Created, result.StatusCode));
        var stored = await catalog.Services.GetRequiredService<IDocumentRepository<ProductQuestion>>().GetAsync(question.Id);
        Assert.Equal(users.Order(), stored!.Answers.Select(a => a.UserId).Order());
        var wrongProduct = await client.PostAsJsonAsync($"/api/products/not-matching/questions/{question.Id}/answers", new { userId = "x", text = "x" });
        Assert.Equal(HttpStatusCode.NotFound, wrongProduct.StatusCode);
    }

    [Fact]
    public async Task Failed_stock_alerts_are_retried_independently_from_persisted_work()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await AssertStockRetryAsync(new InMemoryDocumentRepository<Product>($"retry-products-{suffix}"),
            new InMemoryDocumentRepository<StockAlert>($"retry-alerts-{suffix}"), null);
    }

    [Fact]
    public async Task Retry_worker_recovers_an_alert_without_another_stock_change()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var products = new InMemoryDocumentRepository<Product>($"worker-products-{suffix}");
        var alerts = new InMemoryDocumentRepository<StockAlert>($"worker-alerts-{suffix}");
        var publisher = new FailingPublisher();
        using var provider = new ServiceCollection().AddLogging().AddSingleton<IDocumentRepository<Product>>(products)
            .AddSingleton<IDocumentRepository<StockAlert>>(alerts).AddSingleton<IEventPublisher>(publisher)
            .AddSingleton<BackInStockNotifier>().BuildServiceProvider();
        var product = new Product { Id = "restocked", Name = "Item", Stock = 1,
            BackInStockVersion = 1, BackInStockProductName = "Item", BackInStockQuantity = 1 };
        await products.UpsertAsync(product);
        await alerts.UpsertAsync(new StockAlert { Id = StockAlert.IdFor(product.Id, "fail"), ProductId = product.Id, UserId = "fail" });
        var notifier = provider.GetRequiredService<BackInStockNotifier>();
        await notifier.NotifyIfRestockedAsync(0, product);
        using var worker = new BackInStockRetryService(notifier, NullLogger<BackInStockRetryService>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            for (var i = 0; i < 50 && publisher.Delivered.Count == 0; i++) await Task.Delay(20);
            Assert.Equal(new[] { "fail" }, publisher.Delivered);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [MongoFact]
    public async Task Mongo_failed_stock_alerts_are_retried_independently_from_persisted_work()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("NAKSHATRA_TEST_MONGO"));
        var name = $"nakshatra_alerts_{Guid.NewGuid():N}";
        var database = client.GetDatabase(name);
        try
        {
            await AssertStockRetryAsync(
                new MongoDocumentRepository<Product>(database, ServiceDefaults.CollectionNameFor(typeof(Product))),
                new MongoDocumentRepository<StockAlert>(database, ServiceDefaults.CollectionNameFor(typeof(StockAlert))), database);
        }
        finally { await client.DropDatabaseAsync(name); }
    }

    [Fact]
    public async Task Stock_transition_and_release_dedup_survive_a_missed_notification_scan()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await AssertDurableTransitionAsync(new InMemoryDocumentRepository<Product>($"transition-products-{suffix}"),
            new InMemoryDocumentRepository<StockAlert>($"transition-alerts-{suffix}"), null);
    }

    [Fact]
    public async Task Product_post_upsert_preserves_release_receipts()
    {
        using var catalog = TestHosts.Create<CatalogService>(NoOrderService);
        using var client = catalog.CreateClient();
        var product = await CreateProductAsync(client, 0);
        (await client.PostAsJsonAsync($"/api/products/{product.Id}/release", new { quantity = 2, operationId = "return:1" })).EnsureSuccessStatusCode();
        product.Name = "Updated";
        product.Stock = 0;
        var overwrite = await client.PostAsJsonAsync("/api/products", product, ServiceDefaults.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, overwrite.StatusCode);
        (await client.PostAsJsonAsync($"/api/products/{product.Id}/release", new { quantity = 2, operationId = "return:1" })).EnsureSuccessStatusCode();
        var stored = await client.GetFromJsonAsync<Product>($"/api/products/{product.Id}", ServiceDefaults.JsonOptions);
        Assert.Equal(0, stored!.Stock);
        Assert.Equal("Updated", stored.Name);
    }

    [MongoFact]
    public async Task Mongo_stock_transition_and_release_dedup_survive_a_missed_notification_scan()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("NAKSHATRA_TEST_MONGO"));
        var name = $"nakshatra_transition_{Guid.NewGuid():N}";
        var database = client.GetDatabase(name);
        try
        {
            await AssertDurableTransitionAsync(
                new MongoDocumentRepository<Product>(database, ServiceDefaults.CollectionNameFor(typeof(Product))),
                new MongoDocumentRepository<StockAlert>(database, ServiceDefaults.CollectionNameFor(typeof(StockAlert))), database);
        }
        finally { await client.DropDatabaseAsync(name); }
    }

    private static async Task AssertDurableTransitionAsync(IDocumentRepository<Product> products, IDocumentRepository<StockAlert> alerts, IMongoDatabase? database)
    {
        var events = new CapturingPublisher();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(products).AddSingleton(alerts).AddSingleton<IEventPublisher>(events)
            .AddSingleton<ICacheService, PassThroughCache>().AddSingleton<CatalogProductWrites>().AddSingleton<BackInStockNotifier>();
        if (database is not null) services.AddSingleton(database);
        using var provider = services.BuildServiceProvider();
        var writes = provider.GetRequiredService<CatalogProductWrites>();
        await products.UpsertAsync(new Product { Id = "p", Name = "Item", Stock = 0 });
        if (database is not null)
            await database.GetCollection<Product>(ServiceDefaults.CollectionNameFor(typeof(Product))).UpdateOneAsync(
                p => p.Id == "p", Builders<Product>.Update.Unset(p => p.Stock).Unset(p => p.BackInStockVersion).Unset(p => p.StockReleaseOperations));
        await alerts.UpsertAsync(new StockAlert { Id = "p:first", ProductId = "p", UserId = "first" });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => writes.AdjustAsync("p", 3, "return:one", timeout.Token)));
        Assert.All(results, result => Assert.False(result.Conflict));
        Assert.Equal(3, (await products.GetAsync("p"))!.Stock);
        Assert.True((await writes.AdjustAsync("p", 4, "return:one")).Conflict);
        Assert.False((await writes.AdjustAsync("p", -3, null)).Conflict);
        // Simulate process loss before the original transition's notification scan.
        await provider.GetRequiredService<BackInStockNotifier>().ProcessAsync(null, CancellationToken.None);
        Assert.Equal("first", Assert.Single(events.Events).Payload.GetProperty("userId").GetString());

        var current = (await products.GetAsync("p"))!;
        await alerts.UpsertAsync(new StockAlert { Id = "p:later", ProductId = "p", UserId = "later", SubscribedVersion = current.BackInStockVersion });
        await provider.GetRequiredService<BackInStockNotifier>().ProcessAsync(null, CancellationToken.None);
        Assert.Single(events.Events);
        var updated = new Product { Name = "Changed", Stock = 0 };
        await writes.UpdateAsync("p", updated);
        Assert.False((await writes.AdjustAsync("p", 3, "return:one")).Conflict);
        Assert.Equal(0, (await products.GetAsync("p"))!.Stock);
        await writes.AdjustAsync("p", 1, "return:two");
        await provider.GetRequiredService<BackInStockNotifier>().ProcessAsync(null, CancellationToken.None);
        Assert.Equal("later", events.Events.Last().Payload.GetProperty("userId").GetString());
    }

    private static async Task AssertStockRetryAsync(IDocumentRepository<Product> products, IDocumentRepository<StockAlert> alerts, IMongoDatabase? database)
    {
        var publisher = new FailingPublisher();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(products);
        services.AddSingleton(alerts);
        services.AddSingleton<IEventPublisher>(publisher);
        if (database is not null) services.AddSingleton(database);
        services.AddTransient<BackInStockNotifier>();
        using var provider = services.BuildServiceProvider();
        var product = new Product { Id = "restocked", Name = "Item", Stock = 2,
            BackInStockVersion = 1, BackInStockProductName = "Item", BackInStockQuantity = 2 };
        await products.UpsertAsync(product);
        foreach (var user in new[] { "fail", "succeed" })
            await alerts.UpsertAsync(new StockAlert { Id = StockAlert.IdFor(product.Id, user), ProductId = product.Id, UserId = user });

        await provider.GetRequiredService<BackInStockNotifier>().NotifyIfRestockedAsync(0, product);
        Assert.Equal(new[] { "succeed" }, publisher.Delivered);
        Assert.NotNull(await alerts.GetAsync(StockAlert.IdFor(product.Id, "fail")));
        Assert.Null(await alerts.GetAsync(StockAlert.IdFor(product.Id, "succeed")));

        product.Stock = 0;
        await products.UpsertAsync(product);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => provider.GetRequiredService<BackInStockNotifier>().ProcessAsync(null, CancellationToken.None)));
        Assert.Equal(new[] { "fail", "succeed" }, publisher.Delivered.Order());
        Assert.Null(await alerts.GetAsync(StockAlert.IdFor(product.Id, "fail")));
    }

    private sealed class FailingPublisher : IEventPublisher
    {
        private int failures = 1;
        private readonly ConcurrentQueue<string> delivered = new();
        public IReadOnlyList<string> Delivered => delivered.ToArray();
        public Task PublishAsync<T>(string topic, string key, T payload, CancellationToken ct = default)
        {
            var user = JsonSerializer.SerializeToElement(payload).GetProperty("userId").GetString()!;
            if (user == "fail" && Interlocked.Exchange(ref failures, 0) == 1) throw new InvalidOperationException("temporary failure");
            delivered.Enqueue(user);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Startup_backfills_legacy_seeded_ratings_and_invalidates_cached_products()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var products = new InMemoryDocumentRepository<Product>($"legacy-products-{suffix}");
        var reviews = new InMemoryDocumentRepository<Review>($"legacy-reviews-{suffix}");
        var cache = new IsolatedCache();
        var legacy = new Product { Id = "prod-headphones", Name = "Preserved", Stock = 17, AverageRating = 0, ReviewCount = 0 };
        await products.UpsertAsync(legacy);
        await reviews.UpsertAsync(new Review { Id = "extra-review", ProductId = legacy.Id, Rating = 3 });
        var custom = new Product { Id = $"legacy-{suffix}", Name = "Custom", Stock = 8, ReviewCount = 99, AverageRating = 5 };
        await products.UpsertAsync(custom);
        await reviews.UpsertAsync(new Review { Id = "custom-review", ProductId = custom.Id, Rating = 2 });
        await cache.SetAsync($"product:{legacy.Id}", legacy);
        await cache.SetAsync($"product:{custom.Id}", custom);
        await cache.SetAsync("product:all", new List<Product> { legacy });
        using var catalog = TestHosts.Create<CatalogService>(services =>
        {
            NoOrderService(services);
            services.AddSingleton<IDocumentRepository<Product>>(products);
            services.AddSingleton<IDocumentRepository<Review>>(reviews);
            services.AddSingleton<ICacheService>(cache);
        });
        using var client = catalog.CreateClient();
        var updated = await client.GetFromJsonAsync<Product>($"/api/products/{legacy.Id}", ServiceDefaults.JsonOptions);
        Assert.Equal("Preserved", updated!.Name);
        Assert.Equal(17, updated.Stock);
        Assert.Equal(3, updated.ReviewCount);
        Assert.Equal(4, updated.AverageRating);
        var list = await client.GetFromJsonAsync<List<Product>>("/api/products", ServiceDefaults.JsonOptions);
        Assert.Equal(3, list!.Single(p => p.Id == legacy.Id).ReviewCount);
        var customResult = await client.GetFromJsonAsync<Product>($"/api/products/{custom.Id}", ServiceDefaults.JsonOptions);
        Assert.Equal(1, customResult!.ReviewCount);
        Assert.Equal(2, customResult.AverageRating);
        Assert.Equal(8, customResult.Stock);
    }

    [MongoFact]
    public async Task Mongo_startup_backfills_legacy_seeded_ratings_and_invalidates_cached_products()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("NAKSHATRA_TEST_MONGO"));
        var name = $"nakshatra_ratings_{Guid.NewGuid():N}";
        var database = client.GetDatabase(name);
        try
        {
            var products = new MongoDocumentRepository<Product>(database, ServiceDefaults.CollectionNameFor(typeof(Product)));
            var reviews = new MongoDocumentRepository<Review>(database, ServiceDefaults.CollectionNameFor(typeof(Review)));
            var legacy = new Product { Id = "prod-headphones", Name = "Preserved", Stock = 17 };
            await products.UpsertAsync(legacy);
            await reviews.UpsertAsync(new Review { Id = "extra-review", ProductId = legacy.Id, Rating = 3 });
            await products.UpsertAsync(new Product { Id = "custom-legacy", Stock = 8, ReviewCount = 99, AverageRating = 5 });
            await reviews.UpsertAsync(new Review { Id = "custom-review", ProductId = "custom-legacy", Rating = 2 });
            var cache = new IsolatedCache();
            await cache.SetAsync($"product:{legacy.Id}", legacy);
            await cache.SetAsync("product:all", new List<Product> { legacy });
            using var catalog = TestHosts.Create<CatalogService>(services =>
            {
                NoOrderService(services);
                services.AddSingleton(database);
                services.AddSingleton<IDocumentRepository<Product>>(products);
                services.AddSingleton<IDocumentRepository<Review>>(reviews);
                services.AddSingleton<ICacheService>(cache);
            });
            using var http = catalog.CreateClient();
            var updated = await http.GetFromJsonAsync<Product>($"/api/products/{legacy.Id}", ServiceDefaults.JsonOptions);
            Assert.Equal(17, updated!.Stock);
            Assert.Equal("Preserved", updated.Name);
            Assert.Equal(3, updated.ReviewCount);
            Assert.Equal(4, updated.AverageRating);
            var list = await http.GetFromJsonAsync<List<Product>>("/api/products", ServiceDefaults.JsonOptions);
            Assert.Equal(3, list!.Single(p => p.Id == legacy.Id).ReviewCount);
            var customResult = await http.GetFromJsonAsync<Product>("/api/products/custom-legacy", ServiceDefaults.JsonOptions);
            Assert.Equal(1, customResult!.ReviewCount);
            Assert.Equal(2, customResult.AverageRating);
            Assert.Equal(8, customResult.Stock);
        }
        finally { await client.DropDatabaseAsync(name); }
    }

    [MongoFact]
    public async Task Mongo_backfill_cannot_overwrite_a_newer_review_aggregate()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("NAKSHATRA_TEST_MONGO"));
        var name = $"nakshatra_rating_race_{Guid.NewGuid():N}";
        var database = client.GetDatabase(name);
        try
        {
            var products = new MongoDocumentRepository<Product>(database, ServiceDefaults.CollectionNameFor(typeof(Product)));
            var reviews = new PausedReviews(new MongoDocumentRepository<Review>(database, ServiceDefaults.CollectionNameFor(typeof(Review))));
            await products.UpsertAsync(new Product { Id = "legacy", Stock = 7 });
            await reviews.UpsertAsync(new Review { Id = "one", ProductId = "legacy", Rating = 5 });
            var services = new ServiceCollection().AddSingleton(database).AddSingleton<IDocumentRepository<Product>>(products)
                .AddSingleton<IDocumentRepository<Review>>(reviews).AddSingleton<ICacheService, IsolatedCache>()
                .AddSingleton<ProductRatings>();
            using var provider = services.BuildServiceProvider();
            var ratings = provider.GetRequiredService<ProductRatings>();
            var older = ratings.BackfillAsync();
            await reviews.ReadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await reviews.UpsertAsync(new Review { Id = "two", ProductId = "legacy", Rating = 1 });
            await ratings.RefreshAsync("legacy");
            reviews.Continue.TrySetResult();
            await older.WaitAsync(TimeSpan.FromSeconds(10));
            var stored = await products.GetAsync("legacy");
            Assert.Equal(2, stored!.ReviewCount);
            Assert.Equal(3, stored.AverageRating);
            Assert.Equal(7, stored.Stock);
        }
        finally { await client.DropDatabaseAsync(name); }
    }

    private sealed class PausedReviews(IDocumentRepository<Review> inner) : IDocumentRepository<Review>
    {
        private int paused;
        public TaskCompletionSource ReadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<Review>> ListAsync(CancellationToken ct = default) => inner.ListAsync(ct);
        public Task<Review?> GetAsync(string id, CancellationToken ct = default) => inner.GetAsync(id, ct);
        public async Task<IReadOnlyList<Review>> FindAsync(Expression<Func<Review, bool>> predicate, CancellationToken ct = default)
        {
            var result = await inner.FindAsync(predicate, ct);
            if (Interlocked.Exchange(ref paused, 1) == 0)
            {
                ReadCompleted.TrySetResult();
                await Continue.Task.WaitAsync(ct);
            }
            return result;
        }
        public Task<Review> UpsertAsync(Review value, CancellationToken ct = default) => inner.UpsertAsync(value, ct);
        public Task<bool> TryCreateAsync(Review value, CancellationToken ct = default) => inner.TryCreateAsync(value, ct);
        public Task<bool> TryReplaceAsync(Expression<Func<Review, bool>> predicate, Review value, CancellationToken ct = default)
            => inner.TryReplaceAsync(predicate, value, ct);
        public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
    }

    private sealed class IsolatedCache : ICacheService
    {
        private readonly ConcurrentDictionary<string, string> entries = new();
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(entries.TryGetValue(key, out var value) ? JsonSerializer.Deserialize<T>(value) : default);
        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            entries[key] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken ct = default) { entries.TryRemove(key, out _); return Task.CompletedTask; }
        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            var value = await GetAsync<T>(key, ct);
            if (value is not null) return value;
            value = await factory();
            await SetAsync(key, value, ttl, ct);
            return value;
        }
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
