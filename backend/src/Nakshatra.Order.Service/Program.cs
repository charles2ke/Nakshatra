using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient<CatalogClient>();
builder.Services.AddSingleton<OrderPricing>();
builder.Services.AddHostedService<PaymentStreamConsumer>();

var app = builder.Build();
app.UseNakshatraDefaults("order-service");

// Prices a checkout without placing it: item subtotal, coupon, delivery options, gift wrap and tax.
// An invalid coupon is reported in the quote rather than failing it, so the shopper can fix the code.
app.MapPost("/api/orders/quote", async (
    CreateOrderRequest request,
    IDocumentRepository<Coupon> coupons,
    CatalogClient catalog,
    OrderPricing pricing,
    CancellationToken ct) =>
{
    if (ValidateCheckout(request, requireUser: false) is { } invalid)
    {
        return invalid;
    }

    var (items, lookupError) = await PriceLinesAsync(request.Items, catalog, ct);
    if (lookupError is not null)
    {
        return lookupError;
    }

    var subtotal = items.Sum(i => i.Price * i.Quantity);
    var coupon = string.IsNullOrWhiteSpace(request.CouponCode)
        ? null
        : await Coupons.EvaluateAsync(coupons, Coupons.Normalize(request.CouponCode), subtotal, "USD", ct);
    var discount = coupon?.Discount ?? 0m;
    var speed = request.DeliverySpeed ?? DeliverySpeed.Standard;
    var now = DateTime.UtcNow;
    var price = pricing.Price(subtotal, discount, speed, request.GiftWrap, now);

    return Results.Ok(new
    {
        items,
        price.Subtotal,
        price.Discount,
        couponCode = coupon is { Applied: true } ? coupon.Code : string.Empty,
        couponError = coupon?.Error,
        price.ShippingFee,
        price.GiftWrapFee,
        price.Tax,
        price.Total,
        currency = "USD",
        deliverySpeed = speed,
        price.EstimatedDelivery,
        deliveryOptions = pricing.Options(subtotal - discount, now),
        pricing.FreeShippingThreshold
    });
});

// Orders are persisted in MongoDB and streamed to Kafka for fulfillment, billing and analytics.
app.MapPost("/api/orders", async (
    CreateOrderRequest request,
    IDocumentRepository<Order> repo,
    IDocumentRepository<Coupon> coupons,
    CatalogClient catalog,
    OrderPricing pricing,
    IEventPublisher publisher,
    CancellationToken ct) =>
{
    if (ValidateCheckout(request, requireUser: true) is { } invalid)
    {
        return invalid;
    }

    var (items, lookupError) = await PriceLinesAsync(request.Items, catalog, ct);
    if (lookupError is not null)
    {
        return lookupError;
    }

    var order = new Order
    {
        UserId = request.UserId!,
        Items = items,
        ShippingAddress = request.ShippingAddress ?? string.Empty,
        DeliverySpeed = request.DeliverySpeed ?? DeliverySpeed.Standard,
        GiftWrap = request.GiftWrap,
        GiftMessage = request.GiftMessage?.Trim() ?? string.Empty,
        BuyNow = request.BuyNow
    };

    order.Subtotal = order.Items.Sum(i => i.Price * i.Quantity);
    if (!string.IsNullOrWhiteSpace(request.CouponCode))
    {
        var coupon = await Coupons.EvaluateAsync(coupons, Coupons.Normalize(request.CouponCode), order.Subtotal, order.Currency, ct);
        if (!coupon.Applied)
        {
            return Results.BadRequest(new { error = coupon.Error });
        }

        order.CouponCode = coupon.Code;
        order.Discount = coupon.Discount;
    }

    var price = pricing.Price(order.Subtotal, order.Discount, order.DeliverySpeed, order.GiftWrap, order.CreatedAt);
    order.ShippingFee = price.ShippingFee;
    order.GiftWrapFee = price.GiftWrapFee;
    order.Tax = price.Tax;
    order.Total = price.Total;
    order.EstimatedDelivery = price.EstimatedDelivery;
    order.Status = OrderStatus.AwaitingPayment;
    order.History.Add(new OrderStatusChange { Status = OrderStatus.Created, Note = "Order created." });
    order.History.Add(new OrderStatusChange { Status = OrderStatus.AwaitingPayment, Note = "Awaiting payment authorization." });

    // Reserve before persisting so an order is never accepted for stock that does not exist.
    var reserved = new List<CartItem>();
    foreach (var item in order.Items)
    {
        var reservation = await catalog.ReserveStockAsync(item.ProductId, item.Quantity, ct);
        if (!reservation.Succeeded)
        {
            await catalog.ReleaseStockAsync(reserved);
            return reservation.Status switch
            {
                StockReservationStatus.InsufficientStock => Results.Conflict(new
                {
                    error = reservation.Available > 0
                        ? $"Only {reservation.Available} of '{item.Name}' left in stock."
                        : $"'{item.Name}' is out of stock.",
                    productId = item.ProductId,
                    available = reservation.Available
                }),
                StockReservationStatus.ProductNotFound => Results.BadRequest(new { error = $"Product '{item.ProductId}' was not found." }),
                _ => Results.Json(new { error = "Stock could not be reserved right now. Please try again." }, statusCode: StatusCodes.Status503ServiceUnavailable)
            };
        }

        reserved.Add(item);
    }

    try
    {
        await repo.UpsertAsync(order, ct);
    }
    catch
    {
        await catalog.ReleaseStockAsync(reserved);
        throw;
    }

    await publisher.PublishAsync(Topics.OrdersCreated, order.Id, order, ct);
    return Results.Created($"/api/orders/{order.Id}", order);
});

app.MapGet("/api/orders", async (IDocumentRepository<Order> repo, CancellationToken ct) =>
    Results.Ok((await repo.ListAsync(ct)).OrderByDescending(o => o.CreatedAt).ToList()));

app.MapGet("/api/orders/user/{userId}", async (string userId, IDocumentRepository<Order> repo, CancellationToken ct) =>
    Results.Ok((await repo.FindAsync(o => o.UserId == userId, ct)).OrderByDescending(o => o.CreatedAt).ToList()));

app.MapGet("/api/orders/{id}", async (string id, IDocumentRepository<Order> repo, CancellationToken ct) =>
    await repo.GetAsync(id, ct) is { } order ? Results.Ok(order) : Results.NotFound());

app.MapGet("/api/orders/{id}/status", async (string id, IDocumentRepository<Order> repo, CancellationToken ct) =>
{
    var order = await repo.GetAsync(id, ct);
    return order is null
        ? Results.NotFound()
        : Results.Ok(new { orderId = order.Id, status = order.Status, history = order.History });
});

app.MapPost("/api/orders/{id}/status", async (
    string id,
    UpdateOrderStatusRequest request,
    IDocumentRepository<Order> repo,
    IDocumentRepository<OrderCancellationWork> cancellationWorkRepo,
    IEventPublisher publisher,
    CatalogClient catalog,
    CancellationToken ct) =>
{
    var order = await repo.GetAsync(id, ct);
    if (order is null)
    {
        return Results.NotFound();
    }

    if (!Enum.TryParse<OrderStatus>(request.Status, ignoreCase: true, out var status))
    {
        return Results.BadRequest(new { error = $"Unknown status '{request.Status}'." });
    }

    // Returns restock and refund, so they only go through the return endpoints.
    if (status is OrderStatus.ReturnRequested or OrderStatus.Returned)
    {
        return Results.BadRequest(new { error = "Use the return endpoints to request or complete a return." });
    }

    if (OrderReturns.IsInReturn(order))
    {
        return Results.Conflict(new { error = "Orders being returned cannot change status." });
    }

    if (status == OrderStatus.Cancelled)
    {
        var note = string.IsNullOrWhiteSpace(request.Note) ? "Order cancelled." : request.Note;
        return Results.Ok(await OrderCancellation.CancelAsync(repo, cancellationWorkRepo, publisher, catalog, order, note, ct));
    }

    if (order.Status == OrderStatus.Cancelled)
    {
        return Results.Conflict(new { error = "Cancelled orders cannot change status." });
    }

    var updated = await OrderStatusUpdater.ApplyAsync(repo, publisher, order, status, request.Note ?? string.Empty, ct);
    return Results.Ok(updated);
});

// Customer and admin cancellation. Repeating the call is harmless: stock is only released once.
app.MapPost("/api/orders/{id}/cancel", async (
    string id,
    IDocumentRepository<Order> repo,
    IDocumentRepository<OrderCancellationWork> cancellationWorkRepo,
    IEventPublisher publisher,
    CatalogClient catalog,
    CancellationToken ct) =>
{
    var order = await repo.GetAsync(id, ct);
    if (order is null)
    {
        return Results.NotFound();
    }

    if (order.Status is OrderStatus.Shipped or OrderStatus.Delivered)
    {
        return Results.Conflict(new { error = "Shipped orders cannot be cancelled." });
    }

    if (OrderReturns.IsInReturn(order))
    {
        return Results.Conflict(new { error = "Orders being returned cannot be cancelled." });
    }

    var updated = await OrderCancellation.CancelAsync(repo, cancellationWorkRepo, publisher, catalog, order, "Cancelled by user.", ct);
    return Results.Ok(updated);
});

// A customer asks to return a delivered order within the return window. Asking again is harmless.
app.MapPost("/api/orders/{id}/return", async (
    string id,
    ReturnOrderRequest request,
    IDocumentRepository<Order> repo,
    IEventPublisher publisher,
    CancellationToken ct) =>
{
    var reason = request.Reason?.Trim() ?? string.Empty;
    if (reason.Length is 0 or > OrderReturns.MaxReasonLength)
    {
        return Results.BadRequest(new { error = $"A return reason of 1 to {OrderReturns.MaxReasonLength} characters is required." });
    }

    var order = await repo.GetAsync(id, ct);
    if (order is null)
    {
        return Results.NotFound();
    }

    if (order.Status == OrderStatus.ReturnRequested)
    {
        return Results.Ok(order);
    }

    if (OrderReturns.CheckEligibility(order, DateTime.UtcNow) is { } notEligible)
    {
        return Results.Conflict(new { error = notEligible });
    }

    order.ReturnReason = reason;
    return Results.Ok(await OrderStatusUpdater.ApplyAsync(repo, publisher, order, OrderStatus.ReturnRequested, $"Return requested: {reason}", ct));
});

// The returned goods arrived: restock them and stream Returned, which refunds the payment.
// Repeating the call resumes an interrupted completion without restocking twice.
app.MapPost("/api/orders/{id}/return/complete", async (
    string id,
    IDocumentRepository<Order> repo,
    IDocumentRepository<OrderReturnWork> returnWorkRepo,
    IEventPublisher publisher,
    CatalogClient catalog,
    CancellationToken ct) =>
{
    var order = await repo.GetAsync(id, ct);
    if (order is null)
    {
        return Results.NotFound();
    }

    if (!OrderReturns.IsInReturn(order))
    {
        return Results.Conflict(new { error = "Only orders with a requested return can be completed." });
    }

    return Results.Ok(await OrderReturns.CompleteAsync(repo, returnWorkRepo, publisher, catalog, order, ct));
});

// Promotion codes applied at checkout.
app.MapGet("/api/coupons", async (IDocumentRepository<Coupon> repo, CancellationToken ct) =>
    Results.Ok((await repo.ListAsync(ct)).OrderBy(c => c.Id).ToList()));

app.MapPost("/api/coupons", async (CouponRequest request, IDocumentRepository<Coupon> repo, CancellationToken ct) =>
{
    var code = Coupons.Normalize(request.Code);
    if ((Coupons.ValidateCode(code) ?? Coupons.Validate(request)) is { } error)
    {
        return Results.BadRequest(new { error });
    }

    var coupon = new Coupon { Id = code };
    Coupons.Apply(coupon, request);
    return await repo.TryCreateAsync(coupon, ct)
        ? Results.Created($"/api/coupons/{code}", coupon)
        : Results.Conflict(new { error = $"Coupon '{code}' already exists." });
});

app.MapPut("/api/coupons/{code}", async (string code, CouponRequest request, IDocumentRepository<Coupon> repo, CancellationToken ct) =>
{
    if (Coupons.Validate(request) is { } error)
    {
        return Results.BadRequest(new { error });
    }

    var coupon = await repo.GetAsync(Coupons.Normalize(code), ct);
    if (coupon is null)
    {
        return Results.NotFound();
    }

    Coupons.Apply(coupon, request);
    await repo.UpsertAsync(coupon, ct);
    return Results.Ok(coupon);
});

app.MapDelete("/api/coupons/{code}", async (string code, IDocumentRepository<Coupon> repo, CancellationToken ct) =>
    await repo.DeleteAsync(Coupons.Normalize(code), ct) ? Results.NoContent() : Results.NotFound());

await SeedData.SeedAsync(app.Services, Coupons.Seed);
app.Run();

static IResult? ValidateCheckout(CreateOrderRequest request, bool requireUser)
{
    if ((requireUser && string.IsNullOrWhiteSpace(request.UserId)) || request.Items is not { Count: > 0 })
    {
        return Results.BadRequest(new { error = "An order requires a user and at least one item." });
    }

    if (request.Items.Any(line => line.Quantity <= 0))
    {
        return Results.BadRequest(new { error = "Item quantities must be greater than zero." });
    }

    if (request.DeliverySpeed is { } speed && !Enum.IsDefined(speed))
    {
        return Results.BadRequest(new { error = $"Unknown delivery speed '{speed}'." });
    }

    return request.GiftMessage?.Trim().Length > MaxGiftMessageLength
        ? Results.BadRequest(new { error = $"Gift messages must be at most {MaxGiftMessageLength} characters." })
        : null;
}

// Prices each line from the catalog; the client's prices are never trusted.
static async Task<(List<CartItem> Items, IResult? Error)> PriceLinesAsync(List<CreateOrderLine> lines, CatalogClient catalog, CancellationToken ct)
{
    var items = new List<CartItem>();
    foreach (var line in lines)
    {
        var lookup = await catalog.LookupProductAsync(line.ProductId, ct);
        if (lookup.Status == ProductLookupStatus.Unavailable)
        {
            return (items, Results.Json(new { error = "The catalog could not be reached right now. Please try again." }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        if (lookup.Product is not { } product)
        {
            return (items, Results.BadRequest(new { error = $"Product '{line.ProductId}' was not found." }));
        }

        items.Add(new CartItem
        {
            ProductId = product.Id,
            Name = product.Name,
            Price = product.Price,
            Quantity = line.Quantity
        });
    }

    return (items, null);
}

public record CreateOrderLine(string ProductId, int Quantity);

public record CreateOrderRequest(
    string? UserId,
    List<CreateOrderLine> Items,
    string? ShippingAddress,
    string? CouponCode = null,
    DeliverySpeed? DeliverySpeed = null,
    bool GiftWrap = false,
    string? GiftMessage = null,
    bool BuyNow = false);

public record UpdateOrderStatusRequest(string Status, string? Note);

public partial class Program
{
    private const int MaxGiftMessageLength = 250;
}
