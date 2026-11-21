using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;
using BillingService = Nakshatra.Billing.Service.ServiceMarker;
using CatalogService = Nakshatra.Catalog.Service.ServiceMarker;
using InventoryItem = Nakshatra.Inventory.Service.InventoryItem;
using InventoryService = Nakshatra.Inventory.Service.ServiceMarker;
using OrderEntity = Nakshatra.Shared.Models.Order;
using OrderService = Nakshatra.Order.Service.ServiceMarker;
using PaymentEntity = Nakshatra.Shared.Models.Payment;
using PaymentService = Nakshatra.Payment.Service.ServiceMarker;

namespace Nakshatra.Tests;

/// <summary>Checkout pricing (coupons, delivery speed, gift wrap) and returns.</summary>
public partial class OrderLifecycleTests
{
    [Theory]
    [InlineData(1, null, "Standard", false, "0", "4.99", "0", "1.60", "26.59")]
    [InlineData(2, null, "Standard", false, "0", "0", "0", "3.20", "43.20")]
    [InlineData(2, "welcome10", "Standard", false, "4.00", "0", "0", "2.88", "38.88")]
    [InlineData(2, null, "Expedited", true, "0", "9.99", "3.99", "3.20", "57.18")]
    [InlineData(2, null, "OneDay", false, "0", "14.99", "0", "3.20", "58.19")]
    public async Task Quotes_price_coupons_delivery_speed_and_gift_wrap(
        int quantity, string? coupon, string speed, bool giftWrap,
        string discount, string shipping, string giftWrapFee, string tax, string total)
    {
        using var orders = OrderServiceOverStubCatalog(price: 20m);
        using var client = orders.CreateClient();

        var quote = await QuoteAsync(client, quantity, new { couponCode = coupon, deliverySpeed = speed, giftWrap });

        Assert.Equal(
            new[] { discount, shipping, giftWrapFee, tax, total }.Select(v => decimal.Parse(v, CultureInfo.InvariantCulture)),
            new[] { "discount", "shippingFee", "giftWrapFee", "tax", "total" }.Select(name => quote.GetProperty(name).GetDecimal()));
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("couponError").ValueKind);
        Assert.Equal(3, quote.GetProperty("deliveryOptions").GetArrayLength());
    }

    [Fact]
    public async Task Quotes_explain_why_a_coupon_does_not_apply()
    {
        using var orders = OrderServiceOverStubCatalog(price: 20m);
        using var client = orders.CreateClient();

        var belowMinimum = await QuoteAsync(client, 1, new { couponCode = "SAVE5" });
        var unknown = await QuoteAsync(client, 1, new { couponCode = "NOPE123" });

        Assert.Contains("at least 25.00", belowMinimum.GetProperty("couponError").GetString(), StringComparison.Ordinal);
        Assert.Equal(0m, belowMinimum.GetProperty("discount").GetDecimal());
        Assert.Equal(26.59m, belowMinimum.GetProperty("total").GetDecimal());
        Assert.Contains("not a valid coupon", unknown.GetProperty("couponError").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Orders_charge_and_invoice_the_discounted_total_with_delivery_and_gift_wrap()
    {
        using var catalog = TestHosts.Create<CatalogService>();
        using var orders = OrderServiceUsing(catalog);
        using var payments = TestHosts.Create<PaymentService>(s => s.Route<OrderClient>(() => orders.Server.CreateHandler()));
        using var billing = TestHosts.Create<BillingService>(s => s.Route<OrderClient>(() => orders.Server.CreateHandler()));
        using var catalogClient = catalog.CreateClient();
        using var orderClient = orders.CreateClient();
        var product = await CreateProductAsync(catalogClient, stock: 5);

        var response = await orderClient.PostAsJsonAsync("/api/orders", new
        {
            userId = $"user-{Guid.NewGuid():N}",
            items = new[] { new { productId = product.Id, quantity = 1 } },
            couponCode = "welcome10",
            deliverySpeed = "Expedited",
            giftWrap = true,
            giftMessage = "  Happy birthday!  "
        });
        var order = (await response.Content.ReadFromJsonAsync<OrderEntity>(ServiceDefaults.JsonOptions))!;
        var paid = await payments.CreateClient().PostAsJsonAsync("/api/payments", CardPayment(order));
        var invoice = await billing.CreateClient().GetFromJsonAsync<Invoice>($"/api/billing/invoices/{order.Id}", ServiceDefaults.JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(("WELCOME10", 10m, 9.99m, 3.99m, 7.20m, 111.18m), (order.CouponCode, order.Discount, order.ShippingFee, order.GiftWrapFee, order.Tax, order.Total));
        Assert.Equal("Happy birthday!", order.GiftMessage);
        Assert.Equal(DateTime.UtcNow.Date.AddDays(2), order.EstimatedDelivery);
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        Assert.Equal(OrderStatus.Paid, (await GetOrderAsync(orderClient, order.Id)).Status);
        Assert.Equal(
            new[] { ($"{product.Name} x1", 100m), ("Discount (WELCOME10)", -10m), ("Shipping (Expedited)", 9.99m), ("Gift wrap", 3.99m), ("Tax", 7.20m) },
            invoice!.Lines.Select(l => (l.Description, l.Amount)));
        Assert.Equal(invoice.Total, invoice.Lines.Sum(l => l.Amount));
    }

    [Fact]
    public async Task Invalid_coupons_reject_the_order_before_stock_is_reserved()
    {
        using var catalog = TestHosts.Create<CatalogService>();
        using var orders = OrderServiceUsing(catalog);
        using var catalogClient = catalog.CreateClient();
        using var orderClient = orders.CreateClient();
        var product = await CreateProductAsync(catalogClient, stock: 5);

        var response = await orderClient.PostAsJsonAsync("/api/orders", new
        {
            userId = $"user-{Guid.NewGuid():N}",
            items = new[] { new { productId = product.Id, quantity = 2 } },
            couponCode = "EXPIRED999"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(5, (await GetProductAsync(catalogClient, product.Id)).Stock);
    }

    [Fact]
    public async Task Coupons_can_be_created_deactivated_and_deleted()
    {
        using var orders = OrderServiceOverStubCatalog(price: 20m);
        using var client = orders.CreateClient();
        var code = $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        var created = await client.PostAsJsonAsync("/api/coupons", new { code = code.ToLowerInvariant(), kind = "Fixed", value = 7, minSubtotal = 10 });
        var duplicate = await client.PostAsJsonAsync("/api/coupons", new { code, kind = "Fixed", value = 7 });
        var badCode = await client.PostAsJsonAsync("/api/coupons", new { code = "no!", value = 5 });
        var overHundred = await client.PostAsJsonAsync("/api/coupons", new { code = $"{code}X", kind = "Percent", value = 150 });
        var discounted = await QuoteAsync(client, 1, new { couponCode = code });
        await client.PutAsJsonAsync($"/api/coupons/{code}", new { kind = "Fixed", value = 7, active = false });
        var deactivated = await QuoteAsync(client, 1, new { couponCode = code });
        var deleted = await client.DeleteAsync($"/api/coupons/{code}");
        var deletedAgain = await client.DeleteAsync($"/api/coupons/{code}");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(code, (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badCode.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, overHundred.StatusCode);
        Assert.Equal(7m, discounted.GetProperty("discount").GetDecimal());
        Assert.Equal(0m, deactivated.GetProperty("discount").GetDecimal());
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedAgain.StatusCode);
    }

    [Fact]
    public async Task Returning_a_delivered_order_restocks_and_refunds_without_cancelling_it()
    {
        using var catalog = TestHosts.Create<CatalogService>();
        using var orders = OrderServiceUsing(catalog);
        using var payments = TestHosts.Create<PaymentService>(s => s.Route<OrderClient>(() => orders.Server.CreateHandler()));
        using var catalogClient = catalog.CreateClient();
        using var orderClient = orders.CreateClient();
        using var paymentClient = payments.CreateClient();
        var product = await CreateProductAsync(catalogClient, stock: 5);
        var order = await PlaceOrderAsync(orderClient, product, quantity: 2);
        (await paymentClient.PostAsJsonAsync("/api/payments", CardPayment(order))).EnsureSuccessStatusCode();
        await orderClient.PostAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Shipped" });
        await orderClient.PostAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Delivered" });

        var bypass = await orderClient.PostAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Returned" });
        var requested = await orderClient.PostAsJsonAsync($"/api/orders/{order.Id}/return", new { reason = "Too small" });
        var requestedAgain = await orderClient.PostAsJsonAsync($"/api/orders/{order.Id}/return", new { reason = "Too small" });
        var cancel = await orderClient.PostAsync($"/api/orders/{order.Id}/cancel", null);
        var reship = await orderClient.PostAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Shipped" });
        var completed = await orderClient.PostAsync($"/api/orders/{order.Id}/return/complete", null);
        var completedAgain = await orderClient.PostAsync($"/api/orders/{order.Id}/return/complete", null);

        Assert.Equal(HttpStatusCode.BadRequest, bypass.StatusCode);
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        Assert.Equal(HttpStatusCode.OK, requestedAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reship.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completedAgain.StatusCode);
        Assert.Equal(5, (await GetProductAsync(catalogClient, product.Id)).Stock);
        var payment = Assert.Single((await paymentClient.GetFromJsonAsync<List<PaymentEntity>>($"/api/payments/by-order/{order.Id}", ServiceDefaults.JsonOptions))!);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        var returned = await GetOrderAsync(orderClient, order.Id);
        Assert.Equal(OrderStatus.Returned, returned.Status);
        Assert.Equal("Too small", returned.ReturnReason);
        Assert.Single(returned.History, h => h.Status == OrderStatus.ReturnRequested);
        Assert.Single(returned.History, h => h.Note == $"Payment {payment.Id} refunded.");
    }

    [Theory]
    [InlineData(OrderStatus.Shipped, 1, "Damaged", HttpStatusCode.Conflict)]
    [InlineData(OrderStatus.Delivered, 31, "Damaged", HttpStatusCode.Conflict)]
    [InlineData(OrderStatus.Delivered, 29, " ", HttpStatusCode.BadRequest)]
    [InlineData(OrderStatus.Delivered, 29, "Damaged", HttpStatusCode.OK)]
    public async Task Returns_are_accepted_only_within_the_window_after_delivery(OrderStatus status, int daysAgo, string reason, HttpStatusCode expected)
    {
        using var orders = TestHosts.Create<OrderService>();
        using var client = orders.CreateClient();
        var order = new OrderEntity
        {
            UserId = $"user-{Guid.NewGuid():N}",
            Status = status,
            CreatedAt = DateTime.UtcNow.AddDays(-daysAgo - 3),
            Items = { new CartItem { ProductId = "p-returned", Quantity = 1 } },
            History = { new OrderStatusChange { Status = status, Timestamp = DateTime.UtcNow.AddDays(-daysAgo) } }
        };
        await orders.Services.GetRequiredService<IDocumentRepository<OrderEntity>>().UpsertAsync(order);

        var response = await client.PostAsJsonAsync($"/api/orders/{order.Id}/return", new { reason });

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Inventory_puts_returned_goods_back_on_hand_once_even_after_shipping()
    {
        var productId = Guid.NewGuid().ToString("N");
        var created = new OrderEntity { CreatedAt = DateTime.UtcNow, Items = { new CartItem { ProductId = productId, Quantity = 2 } } };
        var returned = new OrderEntity
        {
            Id = created.Id,
            CreatedAt = created.CreatedAt,
            Status = OrderStatus.Returned,
            Items = { new CartItem { ProductId = productId, Quantity = 2 } },
            History = { new OrderStatusChange { Status = OrderStatus.Shipped }, new OrderStatusChange { Status = OrderStatus.Delivered } }
        };
        using var inventory = TestHosts.Create<InventoryService>();
        var repository = inventory.Services.GetRequiredService<IDocumentRepository<InventoryItem>>();
        await repository.UpsertAsync(new InventoryItem { Id = productId, ProductId = productId, OnHand = 10 });
        var events = inventory.Services.GetRequiredService<IEventPublisher>();

        await events.PublishAsync(Topics.OrdersCreated, created.Id, created);
        var afterSale = (await repository.GetAsync(productId))!.OnHand;
        await events.PublishAsync(Topics.OrdersStatusChanged, created.Id, returned);
        await events.PublishAsync(Topics.OrdersStatusChanged, created.Id, returned);

        Assert.Equal(8, afterSale);
        var item = await repository.GetAsync(productId);
        Assert.Equal(10, item!.OnHand);
        Assert.True(Assert.Single(item.OrderSales).Cancelled);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<OrderService> OrderServiceOverStubCatalog(decimal price)
        => TestHosts.Create<OrderService>(s => s.Route<CatalogClient>(() => new LambdaHttpMessageHandler(request =>
            TestHosts.Json(new Product { Id = request.RequestUri!.Segments[^1], Name = "Quoted", Price = price, Stock = 10 }))));

    private static async Task<JsonElement> QuoteAsync(HttpClient orders, int quantity, object options)
    {
        var body = JsonSerializer.SerializeToNode(options)!.AsObject();
        body["items"] = JsonSerializer.SerializeToNode(new[] { new { productId = "p-quoted", quantity } });
        var response = await orders.PostAsJsonAsync("/api/orders/quote", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
