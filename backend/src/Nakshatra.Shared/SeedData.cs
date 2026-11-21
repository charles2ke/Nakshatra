using Microsoft.Extensions.DependencyInjection;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Shared;

/// <summary>
/// Deterministic demo data so the portal is usable immediately after start-up.
/// Seeding is idempotent: entities keep stable ids and are only written when missing.
/// </summary>
public static class SeedData
{
    public const string VendorAuroraId = "vendor-aurora";
    public const string VendorHelioId = "vendor-helio";

    public static IReadOnlyList<User> Users { get; } = new List<User>
    {
        new() { Id = "user-customer", Name = "Asha Customer", Email = "asha@nakshatra.dev", Persona = Persona.Customer },
        new() { Id = "user-vendor", Name = "Vikram Vendor", Email = "vikram@nakshatra.dev", Persona = Persona.Vendor },
        new() { Id = "user-admin", Name = "Anita Admin", Email = "anita@nakshatra.dev", Persona = Persona.Admin },
        new() { Id = "user-fulfillment", Name = "Farhan Fulfillment", Email = "farhan@nakshatra.dev", Persona = Persona.FulfillmentAgent }
    };

    public static IReadOnlyList<Vendor> Vendors { get; } = new List<Vendor>
    {
        new() { Id = VendorAuroraId, Name = "Aurora Electronics", Email = "sales@aurora.dev", Phone = "+1-202-555-0134", Address = "12 Nebula Way, Seattle", Rating = 4.6 },
        new() { Id = VendorHelioId, Name = "Helio Home", Email = "hello@heliohome.dev", Phone = "+1-202-555-0177", Address = "88 Solar Street, Austin", Rating = 4.2 }
    };

    // Rating aggregates match the seeded reviews below; the catalog recomputes them on every new review.
    public static IReadOnlyList<Product> Products { get; } = new List<Product>
    {
        new() { Id = "prod-headphones", Name = "Nova Wireless Headphones", Description = "Over-ear noise cancelling headphones with 40h battery.", Category = "Electronics", Price = 199.99m, ListPrice = 249.99m, Stock = 42, VendorId = VendorAuroraId, ImageUrl = "https://placehold.co/400x300?text=Headphones", Tags = new() { "audio", "wireless", "travel" }, AverageRating = 4.5, ReviewCount = 2 },
        new() { Id = "prod-earbuds", Name = "Nova Earbuds Pro", Description = "Compact earbuds with adaptive transparency mode.", Category = "Electronics", Price = 129.50m, Stock = 65, VendorId = VendorAuroraId, ImageUrl = "https://placehold.co/400x300?text=Earbuds", Tags = new() { "audio", "wireless" }, AverageRating = 4, ReviewCount = 1 },
        new() { Id = "prod-case", Name = "Headphone Travel Case", Description = "Hard shell case that fits over-ear headphones.", Category = "Accessories", Price = 24.00m, Stock = 120, VendorId = VendorAuroraId, ImageUrl = "https://placehold.co/400x300?text=Case", Tags = new() { "travel", "accessory" } },
        new() { Id = "prod-lamp", Name = "Helio Desk Lamp", Description = "Warm/cool adjustable LED desk lamp with USB-C.", Category = "Home", Price = 59.00m, ListPrice = 79.00m, Stock = 30, VendorId = VendorHelioId, ImageUrl = "https://placehold.co/400x300?text=Lamp", Tags = new() { "lighting", "desk" }, AverageRating = 5, ReviewCount = 1 },
        new() { Id = "prod-chair", Name = "Helio Ergonomic Chair", Description = "Mesh back office chair with lumbar support.", Category = "Home", Price = 349.00m, Stock = 12, VendorId = VendorHelioId, ImageUrl = "https://placehold.co/400x300?text=Chair", Tags = new() { "office", "furniture" } },
        new() { Id = "prod-mug", Name = "Helio Thermal Mug", Description = "Keeps drinks hot for 8 hours.", Category = "Home", Price = 18.75m, ListPrice = 24.99m, Stock = 200, VendorId = VendorHelioId, ImageUrl = "https://placehold.co/400x300?text=Mug", Tags = new() { "kitchen" } }
    };

    public static IReadOnlyList<Review> Reviews { get; } = new List<Review>
    {
        new() { Id = "review-headphones-1", ProductId = "prod-headphones", UserId = "reviewer-meera", Title = "Silence on every flight", Body = "The noise cancelling is excellent and the battery lasted a full week of commuting.", Rating = 5, VerifiedPurchase = true },
        new() { Id = "review-headphones-2", ProductId = "prod-headphones", UserId = "reviewer-arjun", Title = "Comfortable, a little heavy", Body = "Great sound. They get warm after a few hours.", Rating = 4 },
        new() { Id = "review-earbuds-1", ProductId = "prod-earbuds", UserId = "reviewer-meera", Title = "Good transparency mode", Body = "Easy to hear announcements without taking them out.", Rating = 4, VerifiedPurchase = true },
        new() { Id = "review-lamp-1", ProductId = "prod-lamp", UserId = "reviewer-arjun", Title = "Perfect for late nights", Body = "The warm setting is easy on the eyes.", Rating = 5, VerifiedPurchase = true }
    };

    public static async Task SeedAsync<T>(IServiceProvider services, IEnumerable<T> items) where T : class, IEntity
    {
        var repo = services.GetRequiredService<IDocumentRepository<T>>();
        foreach (var item in items)
        {
            if (await repo.GetAsync(item.Id) is null)
            {
                await repo.UpsertAsync(item);
            }
        }
    }
}
