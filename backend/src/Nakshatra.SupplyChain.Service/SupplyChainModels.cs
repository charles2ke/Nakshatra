using Nakshatra.Shared.Models;

namespace Nakshatra.SupplyChain.Service;

/// <summary>Stages an order or replenishment moves through, end to end.</summary>
public enum SupplyChainStage
{
    Sourcing,
    ReplenishmentOrdered,
    GoodsReceived,
    OrderPlaced,
    PaymentSettled,
    Picked,
    Packed,
    HandedToCarrier,
    InTransit,
    OutForDelivery,
    Delivered,
    Exception
}

public class SupplyChainEvent
{
    public SupplyChainStage Stage { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The tracking timeline for one reference (an order id or a replenishment product id).
/// The document id is the tracking reference.
/// </summary>
public class SupplyChainTrace : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = "order";
    public string UserId { get; set; } = string.Empty;
    public SupplyChainStage CurrentStage { get; set; } = SupplyChainStage.Sourcing;
    public List<SupplyChainEvent> Events { get; set; } = new();
    public DateTime? EstimatedDelivery { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
