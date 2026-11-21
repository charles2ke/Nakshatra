using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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
