using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient<CatalogClient>();
builder.Services.AddHostedService<PaymentStreamConsumer>();

var app = builder.Build();
app.UseNakshatraDefaults("order-service");

const decimal TaxRate = 0.08m;

// Orders are persisted in MongoDB and streamed to Kafka for fulfillment, billing and analytics.
app.MapPost("/api/orders", async (
    CreateOrderRequest request,
    IDocumentRepository<Order> repo,
    CatalogClient catalog,
    IEventPublisher publisher,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.UserId) || request.Items.Count == 0)
    {
        return Results.BadRequest(new { error = "An order requires a user and at least one item." });
    }

    var order = new Order
    {
        UserId = request.UserId,
        ShippingAddress = request.ShippingAddress ?? string.Empty
    };

    foreach (var line in request.Items)
    {
        if (line.Quantity <= 0)
        {
            return Results.BadRequest(new { error = "Item quantities must be greater than zero." });
        }

        var product = await catalog.GetProductAsync(line.ProductId, ct);
        if (product is null)
        {
            return Results.BadRequest(new { error = $"Product '{line.ProductId}' was not found." });
        }

        order.Items.Add(new CartItem
        {
            ProductId = product.Id,
            Name = product.Name,
            Price = product.Price,
            Quantity = line.Quantity
        });
    }

    order.Subtotal = order.Items.Sum(i => i.Price * i.Quantity);
    order.Tax = Math.Round(order.Subtotal * TaxRate, 2);
    order.Total = order.Subtotal + order.Tax;
    order.Status = OrderStatus.AwaitingPayment;
    order.History.Add(new OrderStatusChange { Status = OrderStatus.Created, Note = "Order created." });
    order.History.Add(new OrderStatusChange { Status = OrderStatus.AwaitingPayment, Note = "Awaiting payment authorization." });

    await repo.UpsertAsync(order, ct);

    foreach (var item in order.Items)
    {
        await catalog.ReserveStockAsync(item.ProductId, item.Quantity, ct);
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
    IEventPublisher publisher,
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

    var updated = await OrderStatusUpdater.ApplyAsync(repo, publisher, order, status, request.Note ?? string.Empty, ct);
    return Results.Ok(updated);
});

app.MapPost("/api/orders/{id}/cancel", async (
    string id,
    IDocumentRepository<Order> repo,
    IEventPublisher publisher,
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

    var updated = await OrderStatusUpdater.ApplyAsync(repo, publisher, order, OrderStatus.Cancelled, "Cancelled by user.", ct);
    return Results.Ok(updated);
});

app.Run();

public record CreateOrderLine(string ProductId, int Quantity);

public record CreateOrderRequest(string UserId, List<CreateOrderLine> Items, string? ShippingAddress);

public record UpdateOrderStatusRequest(string Status, string? Note);

public partial class Program;
