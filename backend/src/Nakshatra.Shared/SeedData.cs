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

    public static IReadOnlyList<Product> Products { get; } = new List<Product>
    {
        new() { Id = "prod-headphones", Name = "Nova Wireless Headphones", Description = "Over-ear noise cancelling headphones with 40h battery.", Category = "Electronics", Price = 199.99m, Stock = 42, VendorId = VendorAuroraId, ImageUrl = "https://placehold.co/400x300?text=Headphones", Tags = new() { "audio", "wireless", "travel" } },
        new() { Id = "prod-earbuds", Name = "Nova Earbuds Pro", Description = "Compact earbuds with adaptive transparency mode.", Category = "Electronics", Price = 129.50m, Stock = 65, VendorId = VendorAuroraId, ImageUrl = "https://placehold.co/400x300?text=Earbuds", Tags = new() { "audio", "wireless" } },
        new() { Id = "prod-case", Name = "Headphone Travel Case", Description = "Hard shell case that fits over-ear headphones.", Category = "Accessories", Price = 24.00m, Stock = 120, VendorId = VendorAuroraId, ImageUrl = "https://placehold.co/400x300?text=Case", Tags = new() { "travel", "accessory" } },
        new() { Id = "prod-lamp", Name = "Helio Desk Lamp", Description = "Warm/cool adjustable LED desk lamp with USB-C.", Category = "Home", Price = 59.00m, Stock = 30, VendorId = VendorHelioId, ImageUrl = "https://placehold.co/400x300?text=Lamp", Tags = new() { "lighting", "desk" } },
        new() { Id = "prod-chair", Name = "Helio Ergonomic Chair", Description = "Mesh back office chair with lumbar support.", Category = "Home", Price = 349.00m, Stock = 12, VendorId = VendorHelioId, ImageUrl = "https://placehold.co/400x300?text=Chair", Tags = new() { "office", "furniture" } },
        new() { Id = "prod-mug", Name = "Helio Thermal Mug", Description = "Keeps drinks hot for 8 hours.", Category = "Home", Price = 18.75m, Stock = 200, VendorId = VendorHelioId, ImageUrl = "https://placehold.co/400x300?text=Mug", Tags = new() { "kitchen" } }
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
