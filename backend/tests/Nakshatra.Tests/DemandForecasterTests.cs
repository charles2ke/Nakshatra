using Nakshatra.Inventory.Service;

namespace Nakshatra.Tests;

public class DemandForecasterTests
{
    private static List<SalesDataPoint> History(params int[] units)
    {
        var start = new DateOnly(2026, 1, 1);
        return units.Select((u, i) => new SalesDataPoint { Date = start.AddDays(i), Units = u }).ToList();
    }

    [Fact]
    public void Fit_follows_a_rising_series()
    {
        var (level, trend) = DemandForecaster.FitLevelAndTrend(History(10, 11, 12, 13, 14, 15, 16, 17));

        Assert.True(level > 14, $"Expected the level to track the recent values, got {level}.");
        Assert.True(trend > 0, "A rising series should produce a positive trend.");
    }

    [Fact]
    public void Fit_handles_empty_and_single_point_history()
    {
        Assert.Equal((0d, 0d), DemandForecaster.FitLevelAndTrend(new List<SalesDataPoint>()));
        Assert.Equal((7d, 0d), DemandForecaster.FitLevelAndTrend(History(7)));
    }

    [Fact]
    public void Weekday_factors_default_to_one_when_history_is_thin()
    {
        var factors = DemandForecaster.WeekdayFactors(History(5, 5, 5));

        Assert.All(factors, f => Assert.Equal(1.0, f));
    }

    [Fact]
    public void Economic_order_quantity_matches_the_closed_form()
    {
        // 2 * (10 * 365) * 40 / 6 => sqrt(48666.67) ~= 220.6
        var eoq = DemandForecaster.EconomicOrderQuantity(10, 40, 6);

        Assert.InRange(eoq, 220.5, 220.7);
        Assert.Equal(0, DemandForecaster.EconomicOrderQuantity(0, 40, 6));
    }

    [Fact]
    public void Low_stock_triggers_a_reorder_suggestion()
    {
        var item = new InventoryItem
        {
            Id = "prod-x",
            ProductId = "prod-x",
            OnHand = 3,
            LeadTimeDays = 7,
            SalesHistory = History(Enumerable.Repeat(10, 30).ToArray())
        };

        var forecast = DemandForecaster.Forecast(item, horizonDays: 30, today: new DateOnly(2026, 2, 1));

        Assert.True(forecast.ReorderNow);
        Assert.True(forecast.RecommendedOrderQuantity > 0);
        Assert.True(forecast.ReorderPoint > forecast.SafetyStock);
    }

    [Fact]
    public void Deep_stock_does_not_trigger_a_reorder()
    {
        var item = new InventoryItem
        {
            Id = "prod-y",
            ProductId = "prod-y",
            OnHand = 5000,
            SalesHistory = History(Enumerable.Repeat(10, 30).ToArray())
        };

        var forecast = DemandForecaster.Forecast(item, horizonDays: 30, today: new DateOnly(2026, 2, 1));

        Assert.False(forecast.ReorderNow);
        Assert.Equal(0, forecast.RecommendedOrderQuantity);
    }

    [Fact]
    public void Recording_a_sale_reduces_stock_and_buckets_by_day()
    {
        var item = new InventoryItem { Id = "prod-z", ProductId = "prod-z", OnHand = 10 };
        var date = new DateOnly(2026, 3, 1);

        InventoryMath.RecordSale(item, date, 3);
        InventoryMath.RecordSale(item, date, 2);

        Assert.Equal(5, item.OnHand);
        Assert.Single(item.SalesHistory);
        Assert.Equal(5, item.SalesHistory[0].Units);
    }

    [Fact]
    public void Stock_never_goes_negative()
    {
        var item = new InventoryItem { Id = "prod-z", ProductId = "prod-z", OnHand = 1 };

        InventoryMath.RecordSale(item, new DateOnly(2026, 3, 1), 5);

        Assert.Equal(0, item.OnHand);
    }

    [Fact]
    public void Standard_deviation_needs_at_least_two_points()
    {
        Assert.Equal(0, DemandForecaster.StandardDeviation(new List<double> { 4 }));
        Assert.InRange(DemandForecaster.StandardDeviation(new List<double> { 2, 4, 4, 4, 5, 5, 7, 9 }), 2.13, 2.14);
    }
}
