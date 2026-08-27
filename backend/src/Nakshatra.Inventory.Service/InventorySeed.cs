using Nakshatra.Shared;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Inventory.Service;

/// <summary>
/// Seeds stock positions and 120 days of synthetic-but-deterministic sales history so the
/// forecasting endpoints return meaningful numbers on a fresh environment.
/// </summary>
public static class InventorySeed
{
    /// <summary>
    /// Deterministic seed for the synthetic history. String.GetHashCode is randomised per process,
    /// so it cannot be used when the demo data has to look the same on every start-up.
    /// </summary>
    private static int StableSeed(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in value)
            {
                hash = (hash * 31) + c;
            }

            return hash & 0x7FFFFFFF;
        }
    }

    public static async Task EnsureSeededAsync(IServiceProvider services)
    {
        var repo = services.GetRequiredService<IDocumentRepository<InventoryItem>>();
        if ((await repo.ListAsync()).Count > 0)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var product in SeedData.Products)
        {
            var random = new Random(StableSeed(product.Id));
            var baseDemand = Math.Max(2, product.Stock / 20);

            var history = new List<SalesDataPoint>();
            for (var dayOffset = 120; dayOffset >= 1; dayOffset--)
            {
                var date = today.AddDays(-dayOffset);
                // Mild upward trend, a weekend uplift and bounded noise.
                var trend = (120 - dayOffset) * 0.02;
                var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1.4 : 1.0;
                var noise = random.NextDouble() - 0.5;
                var units = (int)Math.Max(0, Math.Round(((baseDemand + trend) * weekend) + noise));
                history.Add(new SalesDataPoint { Date = date, Units = units });
            }

            await repo.UpsertAsync(new InventoryItem
            {
                Id = product.Id,
                ProductId = product.Id,
                VendorId = product.VendorId,
                OnHand = product.Stock,
                LeadTimeDays = 7,
                SalesHistory = history
            });
        }
    }
}
