using System.Net;
using System.Net.Http.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Nakshatra.Shared;
using Nakshatra.Shared.Models;
using OrderService = Nakshatra.Order.Service.ServiceMarker;
using CatalogService = Nakshatra.Catalog.Service.ServiceMarker;

namespace Nakshatra.Tests;

public class CouponAndLegacyProductTests
{
    [Fact]
    public async Task Undefined_coupon_kind_is_rejected_on_create_and_update()
    {
        using var orders = TestHosts.Create<OrderService>();
        using var client = orders.CreateClient();
        var code = $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        var invalidCreate = await client.PostAsJsonAsync("/api/coupons", new { code, kind = 42, value = 5 });
        var created = await client.PostAsJsonAsync("/api/coupons", new { code, kind = "Fixed", value = 5 });
        var invalidUpdate = await client.PutAsJsonAsync($"/api/coupons/{code}", new { kind = 42, value = 7 });
        var coupons = await client.GetFromJsonAsync<List<Coupon>>("/api/coupons", ServiceDefaults.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdate.StatusCode);
        var persisted = Assert.Single(coupons!, c => c.Id == code);
        Assert.Equal(CouponKind.Fixed, persisted.Kind);
        Assert.Equal(5, persisted.Value);
    }

    [Fact]
    public async Task Missing_product_timestamp_is_stable_and_new_products_get_creation_time()
    {
        var document = new BsonDocument { { "_id", $"legacy-{Guid.NewGuid():N}" }, { "Name", "Legacy" } };
        var first = BsonSerializer.Deserialize<Product>(document);
        var second = BsonSerializer.Deserialize<Product>(document);
        Assert.Equal(DateTime.UnixEpoch, first.CreatedAt);
        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, first.CreatedAt.Kind);

        using var catalog = TestHosts.Create<CatalogService>();
        using var client = catalog.CreateClient();
        var before = DateTime.UtcNow;
        var response = await client.PostAsJsonAsync("/api/products", new Product { Name = $"New-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<Product>(ServiceDefaults.JsonOptions);
        Assert.InRange(created!.CreatedAt, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.True(created.CreatedAt > first.CreatedAt);
    }
}
