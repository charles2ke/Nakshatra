using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Cart.Service;

/// <summary>
/// Takes the ordered quantities out of the customer's cart when an order is streamed on
/// <c>orders.created</c>. Anything added to the cart after checkout started is kept, and
/// "Buy Now" orders, which bypass the cart, leave it untouched.
/// </summary>
public class OrderStreamConsumer : EventConsumerBackgroundService
{
    public OrderStreamConsumer(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<OrderStreamConsumer> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.OrdersCreated;

    protected override string ConsumerGroup => "cart-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var order = envelope.As<Order>();
        if (order is null || order.BuyNow || string.IsNullOrWhiteSpace(order.UserId))
        {
            return;
        }

        var repo = scopedServices.GetRequiredService<IDocumentRepository<Nakshatra.Shared.Models.Cart>>();
        await ShoppingState.ChangeCartAsync(repo, order.UserId, false, cart =>
        {
            if (cart.Items.Count == 0) return StateChange.Unchanged;
            foreach (var line in order.Items)
            {
                var item = cart.Items.FirstOrDefault(i => i.ProductId == line.ProductId);
                if (item is null) continue;
                item.Quantity -= line.Quantity;
                if (item.Quantity <= 0) cart.Items.Remove(item);
            }
            return StateChange.Changed;
        }, ct);
    }
}
