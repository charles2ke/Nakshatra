using System.Text.RegularExpressions;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

public enum CouponKind
{
    Percent,
    Fixed
}

/// <summary>A promotion code. The id is the code itself, stored in upper case.</summary>
public class Coupon : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public CouponKind Kind { get; set; } = CouponKind.Percent;
    /// <summary>Percent off (1-100) for <see cref="CouponKind.Percent"/>, an amount off for <see cref="CouponKind.Fixed"/>.</summary>
    public decimal Value { get; set; }
    public decimal MinSubtotal { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool Active { get; set; } = true;
}

public record CouponRequest(string? Code, string? Description, CouponKind? Kind, decimal? Value, decimal? MinSubtotal, DateTime? ExpiresAt, bool? Active);

/// <summary>Result of applying a coupon to a subtotal: either a discount or the reason it does not apply.</summary>
public record CouponEvaluation(string Code, decimal Discount, string? Error)
{
    public bool Applied => Error is null;
}

public static partial class Coupons
{
    public static IReadOnlyList<Coupon> Seed { get; } = new List<Coupon>
    {
        new() { Id = "WELCOME10", Description = "10% off your order", Kind = CouponKind.Percent, Value = 10 },
        new() { Id = "SAVE5", Description = "5 off orders of 25 or more", Kind = CouponKind.Fixed, Value = 5, MinSubtotal = 25 }
    };

    public static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    [GeneratedRegex("^[A-Z0-9]{3,20}$")]
    private static partial Regex CodePattern();

    public static string? ValidateCode(string code) =>
        CodePattern().IsMatch(code) ? null : "Coupon codes are 3 to 20 letters or digits.";

    public static string? Validate(CouponRequest request)
    {
        if (request.Value is not > 0)
        {
            return "The coupon value must be greater than zero.";
        }

        if ((request.Kind ?? CouponKind.Percent) == CouponKind.Percent && request.Value > 100)
        {
            return "A percentage coupon cannot take off more than 100%.";
        }

        if (request.MinSubtotal is < 0)
        {
            return "The minimum subtotal cannot be negative.";
        }

        return request.Description?.Trim().Length > 200 ? "The description must be at most 200 characters." : null;
    }

    public static void Apply(Coupon coupon, CouponRequest request)
    {
        coupon.Description = request.Description?.Trim() ?? string.Empty;
        coupon.Kind = request.Kind ?? CouponKind.Percent;
        coupon.Value = request.Value ?? 0;
        coupon.MinSubtotal = request.MinSubtotal ?? 0;
        coupon.ExpiresAt = request.ExpiresAt;
        coupon.Active = request.Active ?? true;
    }

    public static async Task<CouponEvaluation> EvaluateAsync(
        IDocumentRepository<Coupon> repo,
        string code,
        decimal subtotal,
        string currency,
        CancellationToken ct)
    {
        var coupon = ValidateCode(code) is null ? await repo.GetAsync(code, ct) : null;
        if (coupon is not { Active: true })
        {
            return new CouponEvaluation(code, 0, $"'{code}' is not a valid coupon code.");
        }

        if (coupon.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
        {
            return new CouponEvaluation(code, 0, $"Coupon '{code}' has expired.");
        }

        if (subtotal < coupon.MinSubtotal)
        {
            return new CouponEvaluation(code, 0, $"Coupon '{code}' needs a subtotal of at least {coupon.MinSubtotal:0.00} {currency}.");
        }

        var discount = coupon.Kind == CouponKind.Percent
            ? Math.Round(subtotal * coupon.Value / 100m, 2)
            : Math.Min(coupon.Value, subtotal);
        return new CouponEvaluation(code, discount, null);
    }
}

public record DeliveryOption(DeliverySpeed Speed, decimal Fee, DateTime EstimatedDelivery);

public record PriceBreakdown(
    decimal Subtotal,
    decimal Discount,
    decimal ShippingFee,
    decimal GiftWrapFee,
    decimal Tax,
    decimal Total,
    DateTime EstimatedDelivery);

/// <summary>
/// Checkout pricing: coupon discount, delivery speed fee (standard shipping is free above a threshold),
/// optional gift wrap and sales tax on the discounted subtotal.
/// </summary>
public class OrderPricing(IConfiguration configuration)
{
    public const decimal TaxRate = 0.08m;
    public const decimal GiftWrapPrice = 3.99m;
    public const decimal StandardShippingFee = 4.99m;
    public const decimal ExpeditedShippingFee = 9.99m;
    public const decimal OneDayShippingFee = 14.99m;

    public decimal FreeShippingThreshold { get; } = configuration.GetValue("Shipping:FreeShippingThreshold", 35m);

    public decimal ShippingFee(DeliverySpeed speed, decimal discountedSubtotal) => speed switch
    {
        DeliverySpeed.Expedited => ExpeditedShippingFee,
        DeliverySpeed.OneDay => OneDayShippingFee,
        _ => discountedSubtotal >= FreeShippingThreshold ? 0m : StandardShippingFee
    };

    public static DateTime EstimatedDelivery(DeliverySpeed speed, DateTime now) => now.Date.AddDays(speed switch
    {
        DeliverySpeed.Expedited => 2,
        DeliverySpeed.OneDay => 1,
        _ => 5
    });

    public IReadOnlyList<DeliveryOption> Options(decimal discountedSubtotal, DateTime now) =>
        Enum.GetValues<DeliverySpeed>()
            .Select(speed => new DeliveryOption(speed, ShippingFee(speed, discountedSubtotal), EstimatedDelivery(speed, now)))
            .ToList();

    public PriceBreakdown Price(decimal subtotal, decimal discount, DeliverySpeed speed, bool giftWrap, DateTime now)
    {
        var discounted = subtotal - discount;
        var shipping = ShippingFee(speed, discounted);
        var giftWrapFee = giftWrap ? GiftWrapPrice : 0m;
        var tax = Math.Round(discounted * TaxRate, 2);
        return new PriceBreakdown(subtotal, discount, shipping, giftWrapFee, tax, discounted + shipping + giftWrapFee + tax, EstimatedDelivery(speed, now));
    }
}
