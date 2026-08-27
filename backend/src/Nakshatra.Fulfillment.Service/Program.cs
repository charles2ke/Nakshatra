using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient<OrderClient>();
builder.Services.AddHostedService<OrderStreamConsumer>();

var app = builder.Build();
app.UseNakshatraDefaults("fulfillment-service");

app.MapGet("/api/fulfillment/shipments", async (IDocumentRepository<Shipment> repo, CancellationToken ct) =>
    Results.Ok((await repo.ListAsync(ct)).OrderByDescending(s => s.UpdatedAt).ToList()));

app.MapGet("/api/fulfillment/shipments/{orderId}", async (string orderId, IDocumentRepository<Shipment> repo, CancellationToken ct) =>
    await repo.GetAsync(orderId, ct) is { } shipment ? Results.Ok(shipment) : Results.NotFound());

// Fulfillment agents advance a shipment through Packed -> Shipped -> Delivered.
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

    shipment.Status = shipment.Status + 1;
    shipment.UpdatedAt = DateTime.UtcNow;
    await repo.UpsertAsync(shipment, ct);

    await orders.UpdateStatusAsync(orderId, shipment.Status.ToString(), $"Shipment {shipment.TrackingNumber} is {shipment.Status}.", ct);
    return Results.Ok(shipment);
});

app.Run();

public partial class Program;
