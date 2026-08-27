using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient<OrderClient>();
builder.Services.AddHostedService<OrderStreamConsumer>();
builder.Services.AddHostedService<PaymentStreamConsumer>();

var app = builder.Build();
app.UseNakshatraDefaults("billing-service");

app.MapGet("/api/billing/invoices", async (string? userId, IDocumentRepository<Invoice> repo, CancellationToken ct) =>
{
    var invoices = string.IsNullOrWhiteSpace(userId)
        ? await repo.ListAsync(ct)
        : await repo.FindAsync(i => i.UserId == userId, ct);

    return Results.Ok(invoices.OrderByDescending(i => i.IssuedAt).ToList());
});

app.MapGet("/api/billing/invoices/{orderId}", async (
    string orderId,
    IDocumentRepository<Invoice> repo,
    OrderClient orders,
    CancellationToken ct) =>
{
    var invoice = (await repo.FindAsync(i => i.OrderId == orderId, ct)).FirstOrDefault();
    if (invoice is not null)
    {
        return Results.Ok(invoice);
    }

    // The invoice may not have been streamed yet; fall back to generating it on demand.
    var order = await orders.GetOrderAsync(orderId, ct);
    return order is null
        ? Results.NotFound()
        : Results.Ok(await InvoiceFactory.EnsureInvoiceAsync(repo, order, ct));
});

app.Run();

public partial class Program;
