using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nakshatra.Shared;
using Nakshatra.Shared.Models;

namespace Nakshatra.Tests;

/// <summary>
/// Boots each service in-process. No MongoDB/Redis/Kafka connection strings are configured, so the
/// services fall back to their in-process implementations, which is exactly what the tests need.
/// </summary>
public class ServiceEndpointTests
{
    private static HttpClient CreateClient<TMarker>() where TMarker : class
        => new WebApplicationFactory<TMarker>().CreateClient();

    [Fact]
    public async Task Catalog_returns_seeded_products()
    {
        using var client = CreateClient<Nakshatra.Catalog.Service.ServiceMarker>();

        var products = await client.GetFromJsonAsync<List<Product>>("/api/products", ServiceDefaults.JsonOptions);

        Assert.NotNull(products);
        Assert.Contains(products!, p => p.Id == "prod-headphones");
    }

    [Fact]
    public async Task Catalog_reserve_rejects_oversized_reservation()
    {
        using var client = CreateClient<Nakshatra.Catalog.Service.ServiceMarker>();

        var response = await client.PostAsJsonAsync("/api/products/prod-chair/reserve", new { quantity = 100_000 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Catalog_creates_and_reads_back_a_product()
    {
        using var client = CreateClient<Nakshatra.Catalog.Service.ServiceMarker>();

        var created = await client.PostAsJsonAsync("/api/products", new Product
        {
            Name = "Test Widget",
            Category = "Electronics",
            Price = 9.99m,
            Stock = 5,
            VendorId = SeedData.VendorAuroraId
        }, ServiceDefaults.JsonOptions);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<Product>(ServiceDefaults.JsonOptions);
        var fetched = await client.GetFromJsonAsync<Product>($"/api/products/{product!.Id}", ServiceDefaults.JsonOptions);

        Assert.Equal("Test Widget", fetched!.Name);
    }

    [Fact]
    public async Task Users_expose_personas_and_seeded_users()
    {
        using var client = CreateClient<Nakshatra.User.Service.ServiceMarker>();

        var personas = await client.GetFromJsonAsync<List<string>>("/api/personas");
        var customers = await client.GetFromJsonAsync<List<Nakshatra.Shared.Models.User>>("/api/users/by-persona/Customer", ServiceDefaults.JsonOptions);

        Assert.Contains("FulfillmentAgent", personas!);
        Assert.Contains(customers!, u => u.Id == "user-customer");
    }

    [Fact]
    public async Task Users_reject_unknown_persona()
    {
        using var client = CreateClient<Nakshatra.User.Service.ServiceMarker>();

        var response = await client.GetAsync("/api/users/by-persona/wizard");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Vendors_are_seeded()
    {
        using var client = CreateClient<Nakshatra.Vendor.Service.ServiceMarker>();

        var vendors = await client.GetFromJsonAsync<List<Nakshatra.Shared.Models.Vendor>>("/api/vendors", ServiceDefaults.JsonOptions);

        Assert.Contains(vendors!, v => v.Id == SeedData.VendorAuroraId);
    }

    [Fact]
    public async Task Recommendations_use_the_seeded_co_purchase_graph()
    {
        using var client = CreateClient<Nakshatra.Recommendation.Service.ServiceMarker>();

        var recommendations = await client.GetFromJsonAsync<List<Product>>(
            "/api/recommendations/also-purchased/prod-headphones?limit=2", ServiceDefaults.JsonOptions);

        Assert.NotNull(recommendations);
        Assert.DoesNotContain(recommendations!, p => p.Id == "prod-headphones");
    }

    [Fact]
    public async Task Orders_reject_an_empty_basket()
    {
        using var client = CreateClient<Nakshatra.Order.Service.ServiceMarker>();

        var response = await client.PostAsJsonAsync("/api/orders", new { userId = "user-customer", items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Payments_capture_a_valid_card_and_never_echo_the_pan()
    {
        using var client = CreateClient<Nakshatra.Payment.Service.ServiceMarker>();

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            orderId = "order-test",
            userId = "user-customer",
            amount = 42.5m,
            currency = "USD",
            method = "Card",
            cardNumber = "4242424242424242",
            cardHolder = "Asha Customer",
            expiry = "12/34",
            cvv = "123"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("4242424242424242", body);
        Assert.Contains("**** **** **** 4242", body);
    }

    [Fact]
    public async Task Payments_reject_an_unknown_method()
    {
        using var client = CreateClient<Nakshatra.Payment.Service.ServiceMarker>();

        var response = await client.PostAsJsonAsync("/api/payments", new
        {
            orderId = "order-test",
            userId = "user-customer",
            amount = 10m,
            method = "Barter"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Fulfillment_returns_not_found_for_unknown_shipment()
    {
        using var client = CreateClient<Nakshatra.Fulfillment.Service.ServiceMarker>();

        var response = await client.PostAsync("/api/fulfillment/shipments/missing-order/advance", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Billing_returns_an_empty_invoice_list_for_a_new_user()
    {
        using var client = CreateClient<Nakshatra.Billing.Service.ServiceMarker>();

        var invoices = await client.GetFromJsonAsync<List<Invoice>>("/api/billing/invoices?userId=nobody", ServiceDefaults.JsonOptions);

        Assert.Empty(invoices!);
    }

    [Fact]
    public async Task Health_endpoint_reports_the_service_name()
    {
        using var client = CreateClient<Nakshatra.Search.Service.ServiceMarker>();

        var response = await client.GetStringAsync("/health");

        Assert.Contains("search-service", response);
    }
}
