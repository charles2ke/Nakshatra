using Nakshatra.Payment.Service;
using Nakshatra.Shared;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddPaymentProvider(builder.Configuration);

var app = builder.Build();
app.UseNakshatraDefaults("payment-service");

// Payment gateway: authorizes and immediately captures, then streams the result on Kafka so the
// order service can flip the order to Paid and billing can raise the invoice.
app.MapPost("/api/payments", async (
    PaymentRequest request,
    IDocumentRepository<Payment> repo,
    IEventPublisher publisher,
    IPaymentProvider provider,
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

    var result = await provider.AuthorizeAsync(request, method, ct);
    var payment = new Payment
    {
        OrderId = request.OrderId,
        UserId = request.UserId,
        Amount = request.Amount,
        Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency,
        Method = method,
        MaskedInstrument = result.MaskedInstrument,
        Status = result.Approved ? PaymentStatus.Captured : PaymentStatus.Failed,
        FailureReason = result.FailureReason,
        Provider = provider.Name,
        ProviderReference = result.ProviderReference
    };

    await repo.UpsertAsync(payment, ct);
    await publisher.PublishAsync(Topics.PaymentsCompleted, payment.OrderId, payment, ct);

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

    if (payment.Status != PaymentStatus.Captured)
    {
        return Results.Conflict(new { error = "Only captured payments can be refunded." });
    }

    if (!string.IsNullOrWhiteSpace(payment.Provider) && !string.Equals(payment.Provider, provider.Name, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            error = $"This payment was processed by '{payment.Provider}', but the '{provider.Name}' provider is currently configured. Configure the original provider to process this refund."
        });
    }

    var refund = await provider.RefundAsync(payment, ct);
    if (!refund.Succeeded)
    {
        return Results.BadRequest(new { error = refund.FailureReason });
    }

    payment.Status = PaymentStatus.Refunded;
    await repo.UpsertAsync(payment, ct);
    await publisher.PublishAsync(Topics.PaymentsCompleted, payment.OrderId, payment, ct);
    return Results.Ok(payment);
});

app.Run();

public partial class Program;
