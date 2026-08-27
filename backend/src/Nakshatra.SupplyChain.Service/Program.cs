using Nakshatra.Shared;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Storage;
using Nakshatra.SupplyChain.Service;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddHostedService<OrderCreatedTracker>();
builder.Services.AddHostedService<OrderStatusTracker>();
builder.Services.AddHostedService<ReplenishmentTracker>();

var app = builder.Build();
app.UseNakshatraDefaults("supplychain-service");

// End-to-end tracking timeline for an order or a replenishment reference.
app.MapGet("/api/supplychain/traces/{reference}", async (
    string reference,
    IDocumentRepository<SupplyChainTrace> repo,
    CancellationToken ct) =>
    await repo.GetAsync(reference, ct) is { } trace ? Results.Ok(trace) : Results.NotFound());

app.MapGet("/api/supplychain/traces", async (
    string? userId,
    string? referenceType,
    IDocumentRepository<SupplyChainTrace> repo,
    CancellationToken ct) =>
{
    var traces = await repo.ListAsync(ct);
    var filtered = traces
        .Where(t => string.IsNullOrWhiteSpace(userId) || t.UserId == userId)
        .Where(t => string.IsNullOrWhiteSpace(referenceType) || t.ReferenceType == referenceType)
        .OrderByDescending(t => t.UpdatedAt)
        .ToList();

    return Results.Ok(filtered);
});

// Manual checkpoint, e.g. a warehouse scan or a carrier exception.
app.MapPost("/api/supplychain/traces/{reference}/events", async (
    string reference,
    RecordEventRequest request,
    IDocumentRepository<SupplyChainTrace> repo,
    IEventPublisher publisher,
    CancellationToken ct) =>
{
    if (!Enum.TryParse<SupplyChainStage>(request.Stage, ignoreCase: true, out var stage))
    {
        return Results.BadRequest(new
        {
            error = $"Unknown stage '{request.Stage}'.",
            stages = Enum.GetNames<SupplyChainStage>()
        });
    }

    var trace = await TraceRecorder.RecordAsync(
        repo,
        publisher,
        reference,
        request.ReferenceType ?? "order",
        request.UserId ?? string.Empty,
        stage,
        request.Description ?? stage.ToString(),
        request.Location ?? string.Empty,
        request.Actor ?? "operator",
        request.EstimatedDelivery,
        ct);

    return Results.Ok(trace);
});

app.MapGet("/api/supplychain/stages", () => Results.Ok(Enum.GetNames<SupplyChainStage>()));

app.Run();

public record RecordEventRequest(
    string Stage,
    string? Description,
    string? Location,
    string? Actor,
    string? ReferenceType,
    string? UserId,
    DateTime? EstimatedDelivery);
