using Nakshatra.Shared;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Payment.Service;

public enum RefundStatus
{
    Refunded,
    NotRefundable,
    ProviderMismatch,
    ProviderFailed
}

public record RefundOutcome(RefundStatus Status, string Error = "");

/// <summary>Refund path shared by the refund endpoint and automatic refunds of cancelled orders.</summary>
public static class PaymentRefunds
{
    public static async Task<RefundOutcome> RefundAsync(
        Shared.Models.Payment payment,
        IDocumentRepository<Shared.Models.Payment> repo,
        IEventPublisher publisher,
        IPaymentProvider provider,
        CancellationToken ct = default)
    {
        if (payment.Status != PaymentStatus.Captured)
        {
            return new RefundOutcome(RefundStatus.NotRefundable, "Only captured payments can be refunded.");
        }

        if (!string.IsNullOrWhiteSpace(payment.Provider) && !string.Equals(payment.Provider, provider.Name, StringComparison.OrdinalIgnoreCase))
        {
            return new RefundOutcome(
                RefundStatus.ProviderMismatch,
                $"This payment was processed by '{payment.Provider}', but the '{provider.Name}' provider is currently configured. Configure the original provider to process this refund.");
        }

        var refund = await provider.RefundAsync(payment, ct);
        if (!refund.Succeeded)
        {
            return new RefundOutcome(RefundStatus.ProviderFailed, refund.FailureReason);
        }

        payment.Status = PaymentStatus.Refunded;
        await repo.UpsertAsync(payment, ct);
        await publisher.PublishAsync(Topics.PaymentsCompleted, payment.OrderId, payment, ct);
        return new RefundOutcome(RefundStatus.Refunded);
    }
}

/// <summary>
/// Refunds captured payments automatically when an order is cancelled on <c>orders.status-changed</c>.
/// Refunds that cannot be processed automatically are logged for manual follow-up.
/// </summary>
public class RefundOnCancellationConsumer : EventConsumerBackgroundService
{
    private readonly ILogger<RefundOnCancellationConsumer> _logger;

    public RefundOnCancellationConsumer(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<RefundOnCancellationConsumer> logger)
        : base(subscriber, serviceProvider, logger)
    {
        _logger = logger;
    }

    protected override string Topic => Topics.OrdersStatusChanged;

    protected override string ConsumerGroup => "payment-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var order = envelope.As<Order>();
        if (order is not { Status: OrderStatus.Cancelled })
        {
            return;
        }

        var repo = scopedServices.GetRequiredService<IDocumentRepository<Shared.Models.Payment>>();
        var publisher = scopedServices.GetRequiredService<IEventPublisher>();
        var provider = scopedServices.GetRequiredService<IPaymentProvider>();

        foreach (var payment in await repo.FindAsync(p => p.OrderId == order.Id && p.Status == PaymentStatus.Captured, ct))
        {
            var outcome = await PaymentRefunds.RefundAsync(payment, repo, publisher, provider, ct);
            if (outcome.Status != RefundStatus.Refunded)
            {
                _logger.LogWarning(
                    "Payment {PaymentId} for cancelled order {OrderId} was not refunded automatically: {Reason}",
                    payment.Id, LogSanitizer.Sanitize(order.Id), outcome.Error);
            }
        }
    }
}
