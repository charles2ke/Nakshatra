namespace Nakshatra.Inventory.Service;

/// <summary>
/// Demand forecasting used to optimise stock levels.
///
/// The model is intentionally explainable rather than a black box:
/// * demand level comes from an exponentially weighted moving average (Holt's linear method),
/// * a trend component captures growth or decline,
/// * a day-of-week factor captures weekly seasonality,
/// * safety stock uses the classic <c>z * sigma * sqrt(leadTime)</c> formula,
/// * replenishment size uses the economic order quantity (EOQ).
/// </summary>
public static class DemandForecaster
{
    // Service level z-scores: 95% by default.
    private const double DefaultServiceLevelZ = 1.65;
    private const double Alpha = 0.4;
    private const double Beta = 0.2;

    public static InventoryForecast Forecast(
        InventoryItem item,
        int horizonDays = 30,
        double serviceLevelZ = DefaultServiceLevelZ,
        DateOnly? today = null)
    {
        horizonDays = Math.Clamp(horizonDays, 1, 365);
        var asOf = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var history = item.SalesHistory.OrderBy(p => p.Date).ToList();

        var (level, trend) = FitLevelAndTrend(history);
        var seasonality = WeekdayFactors(history);

        var daily = new List<ForecastPoint>(horizonDays);
        for (var day = 1; day <= horizonDays; day++)
        {
            var date = asOf.AddDays(day);
            var raw = level + (trend * day);
            var factor = seasonality[(int)date.DayOfWeek];
            daily.Add(new ForecastPoint(date, Math.Max(0, raw * factor)));
        }

        var averageDailyDemand = daily.Count == 0 ? 0 : daily.Average(p => p.ForecastUnits);
        var stdDev = StandardDeviation(history.Select(p => (double)p.Units).ToList());
        var forecastDemand = daily.Sum(p => p.ForecastUnits);

        var safetyStock = serviceLevelZ * stdDev * Math.Sqrt(Math.Max(item.LeadTimeDays, 1));
        var reorderPoint = (averageDailyDemand * Math.Max(item.LeadTimeDays, 1)) + safetyStock;
        var eoq = EconomicOrderQuantity(averageDailyDemand, (double)item.OrderingCost, (double)item.HoldingCostPerUnitPerYear);

        var position = item.Available + item.OnOrder;
        var reorderNow = position < reorderPoint;
        var recommendedOrder = reorderNow
            ? (int)Math.Ceiling(Math.Max(eoq, reorderPoint - position))
            : 0;

        var daysOfCover = averageDailyDemand <= 0.0001
            ? int.MaxValue
            : (int)Math.Floor(item.Available / averageDailyDemand);

        return new InventoryForecast(
            item.ProductId,
            horizonDays,
            Math.Round(averageDailyDemand, 3),
            Math.Round(stdDev, 3),
            Math.Round(trend, 4),
            Math.Round(forecastDemand, 2),
            Math.Round(safetyStock, 2),
            Math.Round(reorderPoint, 2),
            Math.Round(eoq, 2),
            recommendedOrder,
            daysOfCover,
            reorderNow,
            daily);
    }

    /// <summary>Holt's linear (double exponential) smoothing of level and trend.</summary>
    public static (double Level, double Trend) FitLevelAndTrend(IReadOnlyList<SalesDataPoint> history)
    {
        if (history.Count == 0)
        {
            return (0, 0);
        }

        if (history.Count == 1)
        {
            return (history[0].Units, 0);
        }

        double level = history[0].Units;
        double trend = history[1].Units - history[0].Units;

        for (var i = 1; i < history.Count; i++)
        {
            var value = history[i].Units;
            var previousLevel = level;
            level = (Alpha * value) + ((1 - Alpha) * (level + trend));
            trend = (Beta * (level - previousLevel)) + ((1 - Beta) * trend);
        }

        return (Math.Max(level, 0), trend);
    }

    /// <summary>Multiplicative day-of-week factors, defaulting to 1.0 when history is thin.</summary>
    public static double[] WeekdayFactors(IReadOnlyList<SalesDataPoint> history)
    {
        var factors = Enumerable.Repeat(1.0, 7).ToArray();
        if (history.Count < 14)
        {
            return factors;
        }

        var overallAverage = history.Average(p => (double)p.Units);
        if (overallAverage <= 0)
        {
            return factors;
        }

        foreach (var group in history.GroupBy(p => (int)p.Date.DayOfWeek))
        {
            var dayAverage = group.Average(p => (double)p.Units);
            // Clamp so a single odd week cannot distort the forecast.
            factors[group.Key] = Math.Clamp(dayAverage / overallAverage, 0.5, 2.0);
        }

        return factors;
    }

    public static double StandardDeviation(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        var mean = values.Average();
        var variance = values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count - 1);
        return Math.Sqrt(variance);
    }

    /// <summary>Classic EOQ: sqrt(2 * annual demand * ordering cost / holding cost).</summary>
    public static double EconomicOrderQuantity(double averageDailyDemand, double orderingCost, double holdingCostPerUnitPerYear)
    {
        if (averageDailyDemand <= 0 || orderingCost <= 0 || holdingCostPerUnitPerYear <= 0)
        {
            return 0;
        }

        var annualDemand = averageDailyDemand * 365;
        return Math.Sqrt(2 * annualDemand * orderingCost / holdingCostPerUnitPerYear);
    }
}
