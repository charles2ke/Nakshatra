using MongoDB.Bson.Serialization.Attributes;

namespace Nakshatra.Shared.Models;

public interface IEntity
{
    string Id { get; set; }
}

public enum Persona
{
    Customer,
    Vendor,
    Admin,
    FulfillmentAgent
}

public class User : IEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Persona Persona { get; set; } = Persona.Customer;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Vendor : IEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Rating { get; set; }
    public bool Active { get; set; } = true;
}

public class Product : IEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int Stock { get; set; }
    public string VendorId { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}

public class CartItem
{
    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}

public class Cart : IEntity
{
    // The cart id is the owning user id so a user has exactly one active cart.
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public List<CartItem> Items { get; set; } = new();
    [BsonIgnore]
    public decimal Subtotal => Items.Sum(i => i.Price * i.Quantity);
}

public enum OrderStatus
{
    Created,
    AwaitingPayment,
    Paid,
    Packed,
    Shipped,
    Delivered,
    Cancelled
}

public class OrderStatusChange
{
    public OrderStatus Status { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Note { get; set; } = string.Empty;
}

public class Order : IEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public List<CartItem> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public OrderStatus Status { get; set; } = OrderStatus.Created;
    public string ShippingAddress { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<OrderStatusChange> History { get; set; } = new();
}

public enum ShipmentStatus
{
    Pending,
    Packed,
    Shipped,
    Delivered
}

public class Shipment : IEntity
{
    // The shipment id is the order id it fulfills.
    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;
    public string Carrier { get; set; } = "Nakshatra Logistics";
    public string TrackingNumber { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum PaymentStatus
{
    Authorized,
    Captured,
    Failed,
    Refunded
}

public enum PaymentMethod
{
    Card,
    UPI,
    NetBanking
}

public class Payment : IEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrderId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentStatus Status { get; set; }
    public PaymentMethod Method { get; set; }
    public string MaskedInstrument { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class InvoiceLine
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class Invoice : IEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrderId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public List<InvoiceLine> Lines { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public bool Paid { get; set; }
}
