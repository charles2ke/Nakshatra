using Nakshatra.Shared.Models;

namespace Nakshatra.Recommendation.Service;

/// <summary>
/// Co-purchase graph for a single product: how often each other product appeared in the same order.
/// The document id is the product id.
/// </summary>
public class CoPurchaseGraph : IEntity
{
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, int> Counts { get; set; } = new();
}
