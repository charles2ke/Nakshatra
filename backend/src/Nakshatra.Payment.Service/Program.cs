using Nakshatra.Payment.Service;
using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddPaymentProvider(builder.Configuration);
builder.Services.AddHttpClient<OrderClient>();
builder.Services.AddHostedService<RefundOnCancellationConsumer>();

var app = builder.Build();
app.UseNakshatraDefaults("payment-service");

// Payment gateway: authorizes and immediately captures, then streams the result on Kafka so the
// order service can flip the order to Paid and billing can raise the invoice.
app.MapPost("/api/payments", async (
    PaymentRequest request,
    IDocumentRepository<Payment> repo,
    IDocumentRepository<PaymentAttempt> attempts,
    IEventPublisher publisher,
    IPaymentProvider provider,
    OrderClient orders,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.OrderId))
    {
        return Results.BadRequest(new { error = "An order id is required." });
    }

    if (!Enum.TryParse<PaymentMethod>(request.Method, ignoreCase: true, out var method))
    {
        return Results.BadRequest(new { error = $"Unsupported payment method '{request.Method}'." });
    }

    // The order service decides what is owed; never charge for an order that cannot be verified.
    var lookup = await orders.LookupOrderAsync(request.OrderId, ct);
    if (lookup.Status == OrderLookupStatus.Unavailable)
    {
        return Results.Json(new { error = "The order could not be verified right now. Please try again." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (lookup.Order is not { } order)
    {
        return Results.NotFound(new { error = "Order was not found." });
    }

    if (order.Status == OrderStatus.Cancelled)
    {
        return Results.Conflict(new { error = "This order was cancelled." });
    }

    if (order.Status is not (OrderStatus.Created or OrderStatus.AwaitingPayment)
        || (await repo.FindAsync(p => p.OrderId == order.Id && p.Status == PaymentStatus.Captured, ct)).Count > 0)
    {
        return Results.Conflict(new { error = "This order has already been paid." });
    }

    if (request.Amount != order.Total)
    {
        return Results.BadRequest(new { error = $"The payment amount must equal the order total of {order.Total} {order.Currency}." });
    }

    if (!string.IsNullOrWhiteSpace(request.Currency) && !string.Equals(request.Currency, order.Currency, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = $"The payment currency must be {order.Currency}." });
    }

    if (!await attempts.TryCreateAsync(new PaymentAttempt { Id = order.Id }, ct))
    {
        return Results.Conflict(new { error = "This order already has a payment attempt in progress or has been paid." });
    }

    var result = await provider.AuthorizeAsync(request with { Currency = order.Currency }, method, ct);
    var payment = new Payment
    {
        OrderId = order.Id,
        UserId = order.UserId,
        Amount = order.Total,
        Currency = order.Currency,
        Method = method,
        MaskedInstrument = result.MaskedInstrument,
        Status = result.Approved ? PaymentStatus.Captured : PaymentStatus.Failed,
        FailureReason = result.FailureReason,
        Provider = provider.Name,
        ProviderReference = result.ProviderReference
    };

    await repo.UpsertAsync(payment, ct);
    await publisher.PublishAsync(Topics.PaymentsCompleted, payment.OrderId, payment, ct);
    if (!result.Approved)
    {
        await attempts.DeleteAsync(order.Id, ct);
    }

    return result.Approved
        ? Results.Created($"/api/payments/{payment.Id}", payment)
        : Results.BadRequest(payment);
});

app.MapGet("/api/payments/{id}", async (string id, IDocumentRepository<Payment> repo, CancellationToken ct) =>
    await repo.GetAsync(id, ct) is { } payment ? Results.Ok(payment) : Results.NotFound());

app.MapGet("/api/payments/by-order/{orderId}", async (string orderId, IDocumentRepository<Payment> repo, CancellationToken ct) =>
    Results.Ok((await repo.FindAsync(p => p.OrderId == orderId, ct)).OrderByDescending(p => p.CreatedAt).ToList()));

app.MapPost("/api/payments/{id}/refund", async (
    string id,
    IDocumentRepository<Payment> repo,
    IEventPublisher publisher,
    IPaymentProvider provider,
    CancellationToken ct) =>
{
    var payment = await repo.GetAsync(id, ct);
    if (payment is null)
    {
        return Results.NotFound();
    }

    var outcome = await PaymentRefunds.RefundAsync(payment, repo, publisher, provider, ct);
    return outcome.Status switch
    {
        RefundStatus.Refunded => Results.Ok(payment),
        RefundStatus.NotRefundable => Results.Conflict(new { error = outcome.Error }),
        _ => Results.BadRequest(new { error = outcome.Error })
    };
});

app.Run();

public partial class Program;
