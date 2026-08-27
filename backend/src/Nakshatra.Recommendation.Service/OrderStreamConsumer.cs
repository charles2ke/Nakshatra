using Nakshatra.Recommendation.Service;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Recommendation.Service;

/// <summary>
/// Consumes the Kafka <c>orders.created</c> stream and keeps the co-purchase graph up to date,
/// which powers the "customers also purchased" strip shown when an item is added to the cart.
/// </summary>
public class OrderStreamConsumer : EventConsumerBackgroundService
{
    public OrderStreamConsumer(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<OrderStreamConsumer> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.OrdersCreated;

    protected override string ConsumerGroup => "recommendation-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var order = envelope.As<Order>();
        if (order is null || order.Items.Count < 2)
        {
            return;
        }

        var repo = scopedServices.GetRequiredService<IDocumentRepository<CoPurchaseGraph>>();
        await CoPurchaseUpdater.ApplyOrderAsync(repo, order.Items.Select(i => i.ProductId).Distinct().ToList(), ct);
    }
}

public static class CoPurchaseUpdater
{
    public static async Task ApplyOrderAsync(IDocumentRepository<CoPurchaseGraph> repo, IReadOnlyList<string> productIds, CancellationToken ct = default)
    {
        foreach (var productId in productIds)
        {
            var graph = await repo.GetAsync(productId, ct) ?? new CoPurchaseGraph { Id = productId };
            foreach (var other in productIds.Where(p => p != productId))
            {
                graph.Counts[other] = graph.Counts.GetValueOrDefault(other) + 1;
            }

            await repo.UpsertAsync(graph, ct);
        }
    }
}
