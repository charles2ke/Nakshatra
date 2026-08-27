using Nakshatra.Shared.Models;

namespace Nakshatra.Inventory.Service;

/// <summary>Units sold for a product on a single day.</summary>
public class SalesDataPoint
{
    public DateOnly Date { get; set; }
    public int Units { get; set; }
}

/// <summary>Stock position and historical demand for one product.</summary>
public class InventoryItem : IEntity
{
    /// <summary>The document id is the product id.</summary>
    public string Id { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string VendorId { get; set; } = string.Empty;
    public string Warehouse { get; set; } = "main";
    public int OnHand { get; set; }
    public int Reserved { get; set; }
    public int OnOrder { get; set; }
    public int LeadTimeDays { get; set; } = 7;
    /// <summary>Fixed cost of raising a replenishment order, used by the EOQ calculation.</summary>
    public decimal OrderingCost { get; set; } = 40m;
    /// <summary>Annual holding cost per unit, used by the EOQ calculation.</summary>
    public decimal HoldingCostPerUnitPerYear { get; set; } = 6m;
    public List<SalesDataPoint> SalesHistory { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int Available => Math.Max(OnHand - Reserved, 0);
}

public record ForecastPoint(DateOnly Date, double ForecastUnits);

public record InventoryForecast(
    string ProductId,
    int HorizonDays,
    double AverageDailyDemand,
    double DemandStdDev,
    double TrendPerDay,
    double ForecastDemand,
    double SafetyStock,
    double ReorderPoint,
    double EconomicOrderQuantity,
    int RecommendedOrderQuantity,
    int DaysOfCoverRemaining,
    bool ReorderNow,
    IReadOnlyList<ForecastPoint> DailyForecast);
