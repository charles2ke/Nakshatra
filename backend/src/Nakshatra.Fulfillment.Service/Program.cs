using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient<OrderClient>();
builder.Services.AddHostedService<OrderStreamConsumer>();
builder.Services.AddHostedService<OrderCancellationConsumer>();

var app = builder.Build();
app.UseNakshatraDefaults("fulfillment-service");

app.MapGet("/api/fulfillment/shipments", async (IDocumentRepository<Shipment> repo, CancellationToken ct) =>
    Results.Ok((await repo.ListAsync(ct)).OrderByDescending(s => s.UpdatedAt).ToList()));

app.MapGet("/api/fulfillment/shipments/{orderId}", async (string orderId, IDocumentRepository<Shipment> repo, CancellationToken ct) =>
    await repo.GetAsync(orderId, ct) is { } shipment ? Results.Ok(shipment) : Results.NotFound());

// Fulfillment agents advance a shipment of a paid order through Packed -> Shipped -> Delivered.
app.MapPost("/api/fulfillment/shipments/{orderId}/advance", async (
    string orderId,
    IDocumentRepository<Shipment> repo,
    OrderClient orders,
    CancellationToken ct) =>
{
    var shipment = await repo.GetAsync(orderId, ct);
    if (shipment is null)
    {
        return Results.NotFound();
    }

    if (shipment.Status == ShipmentStatus.Delivered)
    {
        return Results.Conflict(new { error = "Shipment is already delivered." });
    }

    if (shipment.Status == ShipmentStatus.Cancelled)
    {
        return Results.Conflict(new { error = "This order was cancelled and must not be shipped." });
    }

    // Only paid orders may leave the warehouse; fail closed when the order cannot be checked.
    var lookup = await orders.LookupOrderAsync(orderId, ct);
    if (lookup.Status == OrderLookupStatus.Unavailable)
    {
        return Results.Json(new { error = "The order could not be verified right now. Please try again." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var orderError = lookup.Order?.Status switch
    {
        null => "The order for this shipment was not found.",
        OrderStatus.Cancelled => "This order was cancelled and must not be shipped.",
        OrderStatus.Created or OrderStatus.AwaitingPayment => "This order has not been paid yet.",
        _ => null
    };
    if (orderError is not null)
    {
        return Results.Conflict(new { error = orderError });
    }

    shipment.Status = shipment.Status + 1;
    shipment.UpdatedAt = DateTime.UtcNow;
    await repo.UpsertAsync(shipment, ct);

    await orders.UpdateStatusAsync(orderId, shipment.Status.ToString(), $"Shipment {shipment.TrackingNumber} is {shipment.Status}.", ct);
    return Results.Ok(shipment);
});

app.Run();

public partial class Program;
