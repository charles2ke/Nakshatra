using Nakshatra.Shared.Models;

namespace Nakshatra.Payment.Service;

public class PaymentAttempt : IEntity
{
    public string Id { get; set; } = string.Empty;
}
