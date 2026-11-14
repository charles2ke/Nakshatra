using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nakshatra.Inventory.Service;
using Nakshatra.Payment.Service;
using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;
using CatalogService = Nakshatra.Catalog.Service.ServiceMarker;
using CartEntity = Nakshatra.Shared.Models.Cart;
using OrderEntity = Nakshatra.Shared.Models.Order;
using OrderService = Nakshatra.Order.Service.ServiceMarker;
using PaymentEntity = Nakshatra.Shared.Models.Payment;

namespace Nakshatra.Tests;

/// <summary>
/// Order lifecycle across services: stock reservation, cancellation, payment verification, refunds,
/// fulfillment and cart clean-up.
/// </summary>
public class OrderLifecycleTests
{
    [Fact]
    public async Task Orders_exceeding_stock_are_rejected_and_earlier_reservations_rolled_back()
    {
        using var catalog = TestHosts.Create<CatalogService>();
        using var orders = OrderServiceUsing(catalog);
        using var catalogClient = catalog.CreateClient();
        using var orderClient = orders.CreateClient();
        var plenty = await CreateProductAsync(catalogClient, stock: 5);
        var scarce = await CreateProductAsync(catalogClient, stock: 1);
        var userId = $"user-{Guid.NewGuid():N}";

        var response = await orderClient.PostAsJsonAsync("/api/orders", new
        {
            userId,
            items = new[] { new { productId = plenty.Id, quantity = 2 }, new { productId = scarce.Id, quantity = 3 } }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, error.GetProperty("available").GetInt32());
        Assert.Contains(scarce.Name, error.GetProperty("error").GetString());
        Assert.Equal(5, (await GetProductAsync(catalogClient, plenty.Id)).Stock);
        Assert.Equal(1, (await GetProductAsync(catalogClient, scarce.Id)).Stock);
        Assert.Empty((await orderClient.GetFromJsonAsync<List<OrderEntity>>($"/api/orders/user/{userId}", ServiceDefaults.JsonOptions))!);
    }

    [Fact]
    public async Task Cancelling_returns_stock_once_and_is_final()
    {
        using var catalog = TestHosts.Create<CatalogService>();
        using var orders = OrderServiceUsing(catalog);
        using var catalogClient = catalog.CreateClient();
        using var orderClient = orders.CreateClient();
        var product = await CreateProductAsync(catalogClient, stock: 5);
        var order = await PlaceOrderAsync(orderClient, product, quantity: 2);
        Assert.Equal(3, (await GetProductAsync(catalogClient, product.Id)).Stock);

        var cancelled = await orderClient.PostAsync($"/api/orders/{order.Id}/cancel", null);
        var cancelledAgain = await orderClient.PostAsync($"/api/orders/{order.Id}/cancel", null);
        var reopened = await orderClient.PostAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Paid" });

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancelledAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reopened.StatusCode);
        Assert.Equal(5, (await GetProductAsync(catalogClient, product.Id)).Stock);
        Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(orderClient, order.Id)).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable)]
    public async Task Order_product_lookup_distinguishes_missing_products_from_catalog_outages(
        HttpStatusCode catalogStatus, HttpStatusCode expectedStatus)
    {
        using var orders = TestHosts.Create<OrderService>(services =>
            services.Route<CatalogClient>(() => new StubHttpMessageHandler(catalogStatus, "{}")));
        using var client = orders.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            userId = $"user-{Guid.NewGuid():N}",
            items = new[] { new { productId = "unknown-product", quantity = 1 } }
        });

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task Cancellation_work_retries_publication_and_stock_release_after_failure()
    {
        using var catalog = TestHosts.Create<CatalogService>();
        var publisher = new FailOnceCancellationPublisher();
        using var orders = TestHosts.Create<OrderService>(services =>
        {
            services.Route<CatalogClient>(() => catalog.Server.CreateHandler());
            services.AddSingleton<IEventPublisher>(publisher);
        });
        using var catalogClient = catalog.CreateClient();
        using var orderClient = orders.CreateClient();
        var product = await CreateProductAsync(catalogClient, stock: 5);
        var order = await PlaceOrderAsync(orderClient, product, quantity: 2);

        var firstAttempt = await orderClient.PostAsync($"/api/orders/{order.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.InternalServerError, firstAttempt.StatusCode);
        Assert.Equal(3, (await GetProductAsync(catalogClient, product.Id)).Stock);
        Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(orderClient, order.Id)).Status);

        var retry = await orderClient.PostAsync($"/api/orders/{order.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(5, (await GetProductAsync(catalogClient, product.Id)).Stock);
        Assert.Equal(2, publisher.CancellationPublications);
    }

    [Fact]
    public async Task Cancelling_after_shipment_does_not_restock()
    {
        using var catalog = TestHosts.Create<CatalogService>();
        using var orders = OrderServiceUsing(catalog);
        using var catalogClient = catalog.CreateClient();
        using var orderClient = orders.CreateClient();
        var product = await CreateProductAsync(catalogClient, stock: 5);
        var order = await PlaceOrderAsync(orderClient, product, quantity: 2);
        await orderClient.PostAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Shipped" });

        var customerCancel = await orderClient.PostAsync($"/api/orders/{order.Id}/cancel", null);
        var adminCancel = await orderClient.PostAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Cancelled", note = "Lost in transit." });

        Assert.Equal(HttpStatusCode.Conflict, customerCancel.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminCancel.StatusCode);
        Assert.Equal(3, (await GetProductAsync(catalogClient, product.Id)).Stock);
    }

    [Fact]
    public async Task Cancelling_a_paid_order_refunds_the_payment()
    {
        using var catalog = TestHosts.Create<CatalogService>();
        using var orders = OrderServiceUsing(catalog);
        using var payments = TestHosts.Create<Nakshatra.Payment.Service.ServiceMarker>(s => s.Route<OrderClient>(() => orders.Server.CreateHandler()));
        using var catalogClient = catalog.CreateClient();
        using var orderClient = orders.CreateClient();
        using var paymentClient = payments.CreateClient();
        var product = await CreateProductAsync(catalogClient, stock: 5);
        var order = await PlaceOrderAsync(orderClient, product, quantity: 1);

        var paid = await paymentClient.PostAsJsonAsync("/api/payments", CardPayment(order));
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        Assert.Equal(OrderStatus.Paid, (await GetOrderAsync(orderClient, order.Id)).Status);

        var cancelled = await orderClient.PostAsync($"/api/orders/{order.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var recorded = await paymentClient.GetFromJsonAsync<List<PaymentEntity>>($"/api/payments/by-order/{order.Id}", ServiceDefaults.JsonOptions);
        Assert.Equal(PaymentStatus.Refunded, Assert.Single(recorded!).Status);
        Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(orderClient, order.Id)).Status);
        Assert.Equal(5, (await GetProductAsync(catalogClient, product.Id)).Stock);
    }

    [Fact]
    public async Task Payments_charge_exactly_the_order_total_once()
    {
        var order = TestHosts.AwaitingPayment(108m);
        using var payments = TestHosts.PaymentServiceFor(order);
        using var client = payments.CreateClient();

        var wrongAmount = await client.PostAsJsonAsync("/api/payments", CardPayment(order) with { Amount = 100m });
        var wrongCurrency = await client.PostAsJsonAsync("/api/payments", CardPayment(order) with { Currency = "EUR" });
        var paid = await client.PostAsJsonAsync("/api/payments", CardPayment(order) with { Currency = "usd" });
        var paidTwice = await client.PostAsJsonAsync("/api/payments", CardPayment(order));

        Assert.Equal(HttpStatusCode.BadRequest, wrongAmount.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrency.StatusCode);
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, paidTwice.StatusCode);
        var payment = await paid.Content.ReadFromJsonAsync<PaymentEntity>(ServiceDefaults.JsonOptions);
        Assert.Equal(108m, payment!.Amount);
        Assert.Equal("USD", payment.Currency);
        Assert.Equal(order.UserId, payment.UserId);
    }

    [Fact]
    public async Task Concurrent_payment_requests_claim_an_order_before_authorizing()
    {
        var order = TestHosts.AwaitingPayment(50m);
        var provider = new BlockingPaymentProvider();
        using var payments = TestHosts.Create<Nakshatra.Payment.Service.ServiceMarker>(services =>
        {
            services.Route<OrderClient>(() => new StubHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(order, ServiceDefaults.JsonOptions)));
            services.AddSingleton<IPaymentProvider>(provider);
        });
        using var client = payments.CreateClient();

        var firstRequest = client.PostAsJsonAsync("/api/payments", CardPayment(order));
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondResponse = await client.PostAsJsonAsync("/api/payments", CardPayment(order));
        provider.CompleteAuthorization();
        var firstResponse = await firstRequest;

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(1, provider.AuthorizationCount);
        Assert.Single((await client.GetFromJsonAsync<List<PaymentEntity>>($"/api/payments/by-order/{order.Id}", ServiceDefaults.JsonOptions))!);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, OrderStatus.Cancelled, HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.OK, OrderStatus.Paid, HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.NotFound, OrderStatus.AwaitingPayment, HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, OrderStatus.AwaitingPayment, HttpStatusCode.ServiceUnavailable)]
    public async Task Payments_are_refused_unless_the_order_awaits_payment(HttpStatusCode lookup, OrderStatus status, HttpStatusCode expected)
    {
        var order = TestHosts.AwaitingPayment(50m);
        order.Status = status;
        using var payments = TestHosts.PaymentServiceFor(order, lookup);
        using var client = payments.CreateClient();

        var response = await client.PostAsJsonAsync("/api/payments", CardPayment(order));

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<PaymentEntity>>($"/api/payments/by-order/{order.Id}", ServiceDefaults.JsonOptions))!);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, OrderStatus.AwaitingPayment, HttpStatusCode.Conflict, ShipmentStatus.Pending)]
    [InlineData(HttpStatusCode.OK, OrderStatus.Cancelled, HttpStatusCode.Conflict, ShipmentStatus.Pending)]
    [InlineData(HttpStatusCode.InternalServerError, OrderStatus.Paid, HttpStatusCode.ServiceUnavailable, ShipmentStatus.Pending)]
    [InlineData(HttpStatusCode.OK, OrderStatus.Paid, HttpStatusCode.OK, ShipmentStatus.Packed)]
    public async Task Shipments_only_advance_for_paid_orders(HttpStatusCode lookup, OrderStatus status, HttpStatusCode expected, ShipmentStatus after)
    {
        var order = TestHosts.AwaitingPayment(50m);
        order.Status = status;
        using var fulfillment = TestHosts.Create<Nakshatra.Fulfillment.Service.ServiceMarker>(s =>
            s.Route<OrderClient>(() => new StubHttpMessageHandler(lookup, JsonSerializer.Serialize(order, ServiceDefaults.JsonOptions))));
        using var client = fulfillment.CreateClient();
        await fulfillment.Services.GetRequiredService<IDocumentRepository<Shipment>>()
            .UpsertAsync(new Shipment { Id = order.Id, OrderId = order.Id });

        var response = await client.PostAsync($"/api/fulfillment/shipments/{order.Id}/advance", null);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(after, (await client.GetFromJsonAsync<Shipment>($"/api/fulfillment/shipments/{order.Id}", ServiceDefaults.JsonOptions))!.Status);
    }

    [Fact]
    public async Task Cancelled_orders_take_pending_shipments_off_the_board()
    {
        var order = TestHosts.AwaitingPayment(50m);
        using var fulfillment = TestHosts.Create<Nakshatra.Fulfillment.Service.ServiceMarker>();
        using var client = fulfillment.CreateClient();
        await fulfillment.Services.GetRequiredService<IDocumentRepository<Shipment>>()
            .UpsertAsync(new Shipment { Id = order.Id, OrderId = order.Id, Status = ShipmentStatus.Packed });

        order.Status = OrderStatus.Cancelled;
        await fulfillment.Services.GetRequiredService<IEventPublisher>().PublishAsync(Topics.OrdersStatusChanged, order.Id, order);
        var advance = await client.PostAsync($"/api/fulfillment/shipments/{order.Id}/advance", null);

        Assert.Equal(ShipmentStatus.Cancelled, (await client.GetFromJsonAsync<Shipment>($"/api/fulfillment/shipments/{order.Id}", ServiceDefaults.JsonOptions))!.Status);
        Assert.Equal(HttpStatusCode.Conflict, advance.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancellation_and_creation_events_leave_a_cancelled_shipment(bool cancellationFirst)
    {
        var order = TestHosts.AwaitingPayment(50m);
        order.Status = OrderStatus.Cancelled;
        using var fulfillment = TestHosts.Create<Nakshatra.Fulfillment.Service.ServiceMarker>();
        using var client = fulfillment.CreateClient();
        var events = fulfillment.Services.GetRequiredService<IEventPublisher>();

        if (cancellationFirst)
        {
            await events.PublishAsync(Topics.OrdersStatusChanged, order.Id, order);
            await events.PublishAsync(Topics.OrdersCreated, order.Id, order);
        }
        else
        {
            await events.PublishAsync(Topics.OrdersCreated, order.Id, order);
            await events.PublishAsync(Topics.OrdersStatusChanged, order.Id, order);
        }

        var shipment = await client.GetFromJsonAsync<Shipment>($"/api/fulfillment/shipments/{order.Id}", ServiceDefaults.JsonOptions);
        Assert.Equal(ShipmentStatus.Cancelled, shipment!.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Inventory_reconciles_cancellation_and_creation_in_either_order(bool cancellationFirst)
    {
        var orderId = Guid.NewGuid().ToString("N");
        var productId = Guid.NewGuid().ToString("N");
        var created = new OrderEntity
        {
            Id = orderId,
            CreatedAt = DateTime.UtcNow,
            Status = OrderStatus.AwaitingPayment,
            Items = { new CartItem { ProductId = productId, Quantity = 5 } }
        };
        var cancelled = new OrderEntity
        {
            Id = orderId,
            CreatedAt = created.CreatedAt,
            Status = OrderStatus.Cancelled,
            Items = { new CartItem { ProductId = productId, Quantity = 5 } }
        };
        using var inventory = TestHosts.Create<Nakshatra.Inventory.Service.ServiceMarker>();
        var repository = inventory.Services.GetRequiredService<IDocumentRepository<InventoryItem>>();
        await repository.UpsertAsync(new InventoryItem { Id = productId, ProductId = productId, OnHand = 3 });
        var events = inventory.Services.GetRequiredService<IEventPublisher>();

        if (cancellationFirst)
        {
            await events.PublishAsync(Topics.OrdersStatusChanged, orderId, cancelled);
            await events.PublishAsync(Topics.OrdersCreated, orderId, created);
        }
        else
        {
            await events.PublishAsync(Topics.OrdersCreated, orderId, created);
            await events.PublishAsync(Topics.OrdersStatusChanged, orderId, cancelled);
        }

        var item = await repository.GetAsync(productId);
        Assert.Equal(3, item!.OnHand);
        Assert.Empty(item.SalesHistory);
    }

    [Fact]
    public async Task Placing_an_order_removes_the_ordered_items_from_the_cart()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        using var cartService = TestHosts.Create<Nakshatra.Cart.Service.ServiceMarker>();
        using var client = cartService.CreateClient();
        await cartService.Services.GetRequiredService<IDocumentRepository<CartEntity>>().UpsertAsync(new CartEntity
        {
            Id = userId,
            UserId = userId,
            Items =
            {
                new CartItem { ProductId = "p-ordered-partly", Quantity = 3 },
                new CartItem { ProductId = "p-ordered", Quantity = 1 },
                new CartItem { ProductId = "p-added-later", Quantity = 1 }
            }
        });

        await cartService.Services.GetRequiredService<IEventPublisher>().PublishAsync(Topics.OrdersCreated, "order", new OrderEntity
        {
            UserId = userId,
            Items =
            {
                new CartItem { ProductId = "p-ordered-partly", Quantity = 2 },
                new CartItem { ProductId = "p-ordered", Quantity = 1 }
            }
        });

        var cart = await client.GetFromJsonAsync<CartEntity>($"/api/cart/{userId}", ServiceDefaults.JsonOptions);
        Assert.Equal(
            new[] { ("p-ordered-partly", 1), ("p-added-later", 1) },
            cart!.Items.Select(i => (i.ProductId, i.Quantity)));
    }

    private static WebApplicationFactory<OrderService> OrderServiceUsing(WebApplicationFactory<CatalogService> catalog)
        => TestHosts.Create<OrderService>(s => s.Route<CatalogClient>(() => catalog.Server.CreateHandler()));

    private static CardPaymentRequest CardPayment(OrderEntity order)
        => new(order.Id, order.UserId, order.Total, order.Currency, "Card", "4242424242424242", "Asha Customer", "12/34", "123");

    private static async Task<Product> CreateProductAsync(HttpClient catalog, int stock)
    {
        var response = await catalog.PostAsJsonAsync("/api/products", new Product
        {
            Name = $"Lifecycle {Guid.NewGuid():N}",
            Category = "Electronics",
            Price = 100m,
            Stock = stock,
            VendorId = SeedData.VendorAuroraId
        }, ServiceDefaults.JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Product>(ServiceDefaults.JsonOptions))!;
    }

    private static async Task<Product> GetProductAsync(HttpClient catalog, string id)
        => (await catalog.GetFromJsonAsync<Product>($"/api/products/{id}", ServiceDefaults.JsonOptions))!;

    private static async Task<OrderEntity> GetOrderAsync(HttpClient orders, string id)
        => (await orders.GetFromJsonAsync<OrderEntity>($"/api/orders/{id}", ServiceDefaults.JsonOptions))!;

    private static async Task<OrderEntity> PlaceOrderAsync(HttpClient orders, Product product, int quantity)
    {
        var response = await orders.PostAsJsonAsync("/api/orders", new
        {
            userId = $"user-{Guid.NewGuid():N}",
            items = new[] { new { productId = product.Id, quantity } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderEntity>(ServiceDefaults.JsonOptions))!;
    }

    private sealed record CardPaymentRequest(
        string OrderId, string UserId, decimal Amount, string Currency, string Method,
        string CardNumber, string CardHolder, string Expiry, string Cvv);

    private sealed class FailOnceCancellationPublisher : IEventPublisher
    {
        private int _fail = 1;
        public int CancellationPublications { get; private set; }

        public Task PublishAsync<T>(string topic, string key, T payload, CancellationToken ct = default)
        {
            if (topic == Topics.OrdersStatusChanged)
            {
                CancellationPublications++;
                if (Interlocked.Exchange(ref _fail, 0) == 1)
                {
                    throw new InvalidOperationException("Simulated publication failure.");
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BlockingPaymentProvider : IPaymentProvider
    {
        private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AuthorizationCount { get; private set; }
        public string Name => "test";

        public async Task<GatewayResult> AuthorizeAsync(PaymentRequest request, PaymentMethod method, CancellationToken ct = default)
        {
            AuthorizationCount++;
            Started.TrySetResult(true);
            await _completion.Task.WaitAsync(ct);
            return new GatewayResult(true, "Card on file", string.Empty);
        }

        public Task<RefundResult> RefundAsync(PaymentEntity payment, CancellationToken ct = default)
            => Task.FromResult(new RefundResult(true, string.Empty));

        public void CompleteAuthorization() => _completion.TrySetResult(true);
    }
}
