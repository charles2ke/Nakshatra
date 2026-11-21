using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Nakshatra.Shared;
using Nakshatra.Shared.Caching;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;
using CartEntity = Nakshatra.Shared.Models.Cart;
using CartService = Nakshatra.Cart.Service.ServiceMarker;
using OrderEntity = Nakshatra.Shared.Models.Order;

namespace Nakshatra.Tests;

/// <summary>Save for later, wish lists and Buy Now.</summary>
public class CartAndWishListTests
{
    [Fact]
    public async Task Saved_items_survive_checkout_and_return_to_the_cart_at_the_current_price()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var catalog = new FakeCatalog(new Product { Id = "p-lamp", Name = "Lamp", Price = 42m, Stock = 3 });
        using var cartService = CartServiceOver(catalog);
        using var client = cartService.CreateClient();
        await cartService.Services.GetRequiredService<IDocumentRepository<CartEntity>>().UpsertAsync(new CartEntity
        {
            Id = userId,
            UserId = userId,
            Items =
            {
                new CartItem { ProductId = "p-lamp", Name = "Lamp", Price = 50m, Quantity = 2 },
                new CartItem { ProductId = "p-mug", Name = "Mug", Price = 9m, Quantity = 1 }
            }
        });

        var saved = await client.PostAsync($"/api/cart/{userId}/items/p-lamp/save-for-later", null);
        var checkedOut = await client.DeleteFromJsonAsync<CartEntity>($"/api/cart/{userId}", ServiceDefaults.JsonOptions);
        var moved = await client.PostAsync($"/api/cart/{userId}/saved/p-lamp/move-to-cart", null);
        var missing = await client.PostAsync($"/api/cart/{userId}/saved/p-lamp/move-to-cart", null);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Empty(checkedOut!.Items);
        Assert.Equal("p-lamp", Assert.Single(checkedOut.SavedForLater).ProductId);
        var cart = await moved.Content.ReadFromJsonAsync<CartEntity>(ServiceDefaults.JsonOptions);
        Assert.Empty(cart!.SavedForLater);
        var line = Assert.Single(cart.Items);
        Assert.Equal((2, 42m), (line.Quantity, line.Price));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Wish_lists_show_live_prices_and_move_items_to_the_cart()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var kettle = new Product { Id = $"p-{Guid.NewGuid():N}", Name = "Kettle", Price = 50m, Currency = "USD", Stock = 4 };
        var catalog = new FakeCatalog(kettle);
        using var cartService = CartServiceOver(catalog);
        using var client = cartService.CreateClient();
        var wishList = $"/api/wishlist/{userId}";

        await client.PostAsJsonAsync($"{wishList}/items", new { productId = kettle.Id });
        var addedTwice = await client.PostAsJsonAsync($"{wishList}/items", new { productId = kettle.Id });
        var unknown = await client.PostAsJsonAsync($"{wishList}/items", new { productId = "p-missing" });
        kettle.Price = 45m;
        var view = await client.GetFromJsonAsync<JsonElement>(wishList);
        catalog.Down = true;
        var duringOutage = await client.PostAsJsonAsync($"{wishList}/items", new { productId = "p-other" });
        catalog.Down = false;
        var moved = await client.PostAsync($"{wishList}/items/{kettle.Id}/move-to-cart", null);

        Assert.Equal(1, (await addedTwice.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, duringOutage.StatusCode);
        var entry = Assert.Single(view.GetProperty("items").EnumerateArray());
        Assert.Equal(50m, entry.GetProperty("priceWhenAdded").GetDecimal());
        Assert.Equal(45m, entry.GetProperty("price").GetDecimal());
        Assert.True(entry.GetProperty("priceDropped").GetBoolean());
        var result = await moved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, result.GetProperty("wishList").GetProperty("items").GetArrayLength());
        var line = Assert.Single(result.GetProperty("cart").GetProperty("items").EnumerateArray());
        Assert.Equal(45m, line.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Buy_now_orders_leave_the_cart_untouched()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        using var cartService = TestHosts.Create<CartService>();
        var carts = cartService.Services.GetRequiredService<IDocumentRepository<CartEntity>>();
        await carts.UpsertAsync(new CartEntity { Id = userId, UserId = userId, Items = { new CartItem { ProductId = "p-in-cart", Quantity = 1 } } });

        await cartService.Services.GetRequiredService<IEventPublisher>().PublishAsync(Topics.OrdersCreated, "order", new OrderEntity
        {
            UserId = userId,
            BuyNow = true,
            Items = { new CartItem { ProductId = "p-in-cart", Quantity = 1 } }
        });

        Assert.Equal("p-in-cart", Assert.Single((await carts.GetAsync(userId))!.Items).ProductId);
    }

    [Fact]
    public async Task In_memory_moves_survive_failures_concurrency_and_readding()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await ExerciseMoveRecoveryAsync(
            new InMemoryDocumentRepository<CartEntity>($"carts-{suffix}"),
            new InMemoryDocumentRepository<WishList>($"wishlists-{suffix}"));
    }

    [DiscoveryTests.MongoFact]
    public async Task Mongo_moves_survive_failures_concurrency_and_readding()
    {
        var client = new MongoClient(Environment.GetEnvironmentVariable("NAKSHATRA_TEST_MONGO"));
        var name = $"nakshatra_cart_{Guid.NewGuid():N}";
        var database = client.GetDatabase(name);
        try
        {
            var carts = new MongoDocumentRepository<CartEntity>(database, "carts");
            var lists = new MongoDocumentRepository<WishList>(database, "wishlists");
            await ExerciseMoveRecoveryAsync(carts, lists);
            var user = $"legacy-{Guid.NewGuid():N}";
            await carts.UpsertAsync(new CartEntity { Id = user, UserId = user });
            await lists.UpsertAsync(new WishList
            {
                Id = user, UserId = user,
                Items = { new WishListItem { ProductId = "legacy-product" } }
            });
            var filter = Builders<BsonDocument>.Filter.Eq("_id", user);
            await database.GetCollection<BsonDocument>("carts").UpdateOneAsync(filter, Builders<BsonDocument>.Update.Unset("Revision").Unset("AppliedWishListMoves"));
            await database.GetCollection<BsonDocument>("wishlists").UpdateOneAsync(filter, Builders<BsonDocument>.Update.Unset("Revision").Unset("Items.0.EntryId"));
            using var host = TestHosts.Create<CartService>(services =>
            {
                services.AddSingleton<ICacheService, PassThroughCache>();
                services.Route<CatalogClient>(() => new LambdaHttpMessageHandler(
                    new FakeCatalog(new Product { Id = "legacy-product", Name = "Legacy", Price = 1m }).Respond));
                services.AddSingleton<IDocumentRepository<CartEntity>>(carts);
                services.AddSingleton<IDocumentRepository<WishList>>(lists);
            });
            using var http = host.CreateClient();
            (await http.PostAsync($"/api/wishlist/{user}/items/legacy-product/move-to-cart", null)).EnsureSuccessStatusCode();
            Assert.Equal(1, Assert.Single((await carts.GetAsync(user))!.Items).Quantity);
            Assert.Empty((await lists.GetAsync(user))!.Items);
        }
        finally { await client.DropDatabaseAsync(name); }
    }

    private static async Task ExerciseMoveRecoveryAsync(
        IDocumentRepository<CartEntity> cartStore, IDocumentRepository<WishList> wishStore)
    {
        var cartFault = new FaultingRepository<CartEntity>(cartStore);
        var wishFault = new FaultingRepository<WishList>(wishStore);
        var product = new Product { Id = "recovery-product", Name = "Product", Price = 12m, Stock = 10 };
        var otherProduct = new Product { Id = "recovery-other", Name = "Other", Price = 8m, Stock = 10 };
        var catalog = new FakeCatalog(product, otherProduct);
        using var host = TestHosts.Create<CartService>(services =>
        {
            services.AddSingleton<ICacheService, PassThroughCache>();
            services.Route<CatalogClient>(() => new LambdaHttpMessageHandler(catalog.Respond));
            services.AddSingleton<IDocumentRepository<CartEntity>>(cartFault);
            services.AddSingleton<IDocumentRepository<WishList>>(wishFault);
        });
        using var client = host.CreateClient();

        foreach (var fault in new[] { "cart-after", "wish-before", "wish-after" })
        {
            var user = $"user-{Guid.NewGuid():N}";
            var path = $"/api/wishlist/{user}/items/{product.Id}/move-to-cart";
            await cartStore.UpsertAsync(new CartEntity
            {
                Id = user, UserId = user,
                Items = { new CartItem { ProductId = product.Id, Quantity = 3 } }
            });
            (await client.PostAsJsonAsync($"/api/wishlist/{user}/items", new { productId = product.Id })).EnsureSuccessStatusCode();
            if (fault == "cart-after") cartFault.FailAfter = true;
            if (fault == "wish-before") wishFault.FailBefore = true;
            if (fault == "wish-after") wishFault.FailAfter = true;
            try { await client.PostAsync(path, null); } catch (HttpRequestException) { } catch (InvalidOperationException) { }
            Assert.False(cartFault.FailAfter);
            Assert.False(wishFault.FailBefore);
            Assert.False(wishFault.FailAfter);
            if (fault == "cart-after") catalog.Down = true;
            var retry = await client.PostAsync(path, null);
            catalog.Down = false;
            Assert.Contains(retry.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
            Assert.Equal(4, Assert.Single((await cartStore.GetAsync(user))!.Items).Quantity);
            Assert.Empty((await wishStore.GetAsync(user))!.Items);

            // A new entry for the same product is a new move, not a retry.
            (await client.PostAsJsonAsync($"/api/wishlist/{user}/items", new { productId = product.Id })).EnsureSuccessStatusCode();
            (await client.PostAsync(path, null)).EnsureSuccessStatusCode();
            Assert.Equal(5, Assert.Single((await cartStore.GetAsync(user))!.Items).Quantity);
        }

        var concurrentUser = $"user-{Guid.NewGuid():N}";
        var url = $"/api/wishlist/{concurrentUser}/items/{product.Id}/move-to-cart";
        (await client.PostAsJsonAsync($"/api/wishlist/{concurrentUser}/items", new { productId = product.Id })).EnsureSuccessStatusCode();
        cartFault.SynchronizeNextTwoWrites();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.PostAsync(url, null)));
        Assert.Equal(1, Assert.Single((await cartStore.GetAsync(concurrentUser))!.Items).Quantity);
        Assert.Empty((await wishStore.GetAsync(concurrentUser))!.Items);

        var mixedUser = $"user-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync($"/api/wishlist/{mixedUser}/items", new { productId = product.Id });
        await client.PostAsJsonAsync($"/api/wishlist/{mixedUser}/items", new { productId = otherProduct.Id });
        cartFault.SynchronizeNextTwoWrites();
        await Task.WhenAll(
            client.PostAsync($"/api/wishlist/{mixedUser}/items/{product.Id}/move-to-cart", null),
            client.PostAsync($"/api/wishlist/{mixedUser}/items/{otherProduct.Id}/move-to-cart", null));
        var mixed = (await cartStore.GetAsync(mixedUser))!;
        Assert.Equal(2, mixed.Items.Count);
        Assert.All(mixed.Items, item => Assert.Equal(1, item.Quantity));
        Assert.Empty((await wishStore.GetAsync(mixedUser))!.Items);

        var legacyUser = $"user-{Guid.NewGuid():N}";
        await wishStore.UpsertAsync(new WishList
        {
            Id = legacyUser, UserId = legacyUser,
            Items = { new WishListItem { ProductId = product.Id } }
        });
        (await client.PostAsync($"/api/wishlist/{legacyUser}/items/{product.Id}/move-to-cart", null)).EnsureSuccessStatusCode();
        Assert.Equal(1, Assert.Single((await cartStore.GetAsync(legacyUser))!.Items).Quantity);
    }

    private sealed class FaultingRepository<T>(IDocumentRepository<T> inner) : IDocumentRepository<T> where T : class, IEntity
    {
        public bool FailBefore { get; set; }
        public bool FailAfter { get; set; }
        private TaskCompletionSource? _writeGate;
        private int _writeCount;
        public void SynchronizeNextTwoWrites()
        {
            _writeCount = 0;
            _writeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        public Task<T?> GetAsync(string id, CancellationToken ct = default) => inner.GetAsync(id, ct);
        public Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default) => inner.ListAsync(ct);
        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) => inner.FindAsync(predicate, ct);
        public Task<T> UpsertAsync(T entity, CancellationToken ct = default) => inner.UpsertAsync(entity, ct);
        public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
        public Task<bool> TryCreateAsync(T entity, CancellationToken ct = default) => Write(() => inner.TryCreateAsync(entity, ct));
        public Task<bool> TryReplaceAsync(Expression<Func<T, bool>> predicate, T entity, CancellationToken ct = default)
            => Write(() => inner.TryReplaceAsync(predicate, entity, ct));

        private async Task<bool> Write(Func<Task<bool>> write)
        {
            var gate = _writeGate;
            if (gate is not null)
            {
                if (Interlocked.Increment(ref _writeCount) == 2) gate.TrySetResult();
                await gate.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            if (FailBefore) { FailBefore = false; throw new InvalidOperationException("Injected before write."); }
            var result = await write();
            if (FailAfter) { FailAfter = false; throw new InvalidOperationException("Injected after write."); }
            return result;
        }
    }

    private static WebApplicationFactory<CartService> CartServiceOver(FakeCatalog catalog)
        => TestHosts.Create<CartService>(services =>
        {
            services.AddSingleton<ICacheService, PassThroughCache>();
            services.Route<CatalogClient>(() => new LambdaHttpMessageHandler(catalog.Respond));
        });

    /// <summary>A catalog whose products and availability the test can change between calls.</summary>
    private sealed class FakeCatalog(params Product[] products)
    {
        public bool Down { get; set; }

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            if (Down)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/products")
            {
                return TestHosts.Json(products);
            }

            var product = products.FirstOrDefault(p => path == $"/api/products/{p.Id}");
            return product is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : TestHosts.Json(product);
        }
    }
}
