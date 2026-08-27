using Nakshatra.Shared.Models;

namespace Nakshatra.Payment.Service;

public record PaymentRequest(
    string OrderId,
    string UserId,
    decimal Amount,
    string? Currency,
    string Method,
    string? CardNumber,
    string? CardHolder,
    string? Expiry,
    string? Cvv);

public record GatewayResult(bool Approved, string MaskedInstrument, string FailureReason);

/// <summary>
/// Simulated payment gateway. Validates the instrument and approves the charge, except for the
/// well-known decline test card (any card number ending in 0000).
/// </summary>
public static class PaymentGateway
{
    public static GatewayResult Authorize(PaymentRequest request, PaymentMethod method)
    {
        if (request.Amount <= 0)
        {
            return new GatewayResult(false, string.Empty, "Amount must be greater than zero.");
        }

        if (method != PaymentMethod.Card)
        {
            return new GatewayResult(true, $"{method} account", string.Empty);
        }

        var digits = new string((request.CardNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is < 12 or > 19)
        {
            return new GatewayResult(false, string.Empty, "Card number is invalid.");
        }

        if (!IsLuhnValid(digits))
        {
            return new GatewayResult(false, string.Empty, "Card number failed checksum validation.");
        }

        if (!IsExpiryValid(request.Expiry))
        {
            return new GatewayResult(false, string.Empty, "Card expiry is invalid or in the past.");
        }

        var cvv = request.Cvv ?? string.Empty;
        if (cvv.Length is < 3 or > 4 || !cvv.All(char.IsDigit))
        {
            return new GatewayResult(false, string.Empty, "CVV is invalid.");
        }

        var masked = $"**** **** **** {digits[^4..]}";
        return digits.EndsWith("0000", StringComparison.Ordinal)
            ? new GatewayResult(false, masked, "Card was declined by the issuer.")
            : new GatewayResult(true, masked, string.Empty);
    }

    public static bool IsLuhnValid(string digits)
    {
        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var value = digits[i] - '0';
            if (alternate)
            {
                value *= 2;
                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    public static bool IsExpiryValid(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            return false;
        }

        var parts = expiry.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var month) || !int.TryParse(parts[1], out var year))
        {
            return false;
        }

        if (month is < 1 or > 12)
        {
            return false;
        }

        if (year < 100)
        {
            year += 2000;
        }

        var lastDayOfMonth = new DateTime(year, month, DateTime.DaysInMonth(year, month), 23, 59, 59, DateTimeKind.Utc);
        return lastDayOfMonth >= DateTime.UtcNow;
    }
}
