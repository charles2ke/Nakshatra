using Nakshatra.Payment.Service;
using Nakshatra.Shared.Models;

namespace Nakshatra.Tests;

public class PaymentGatewayTests
{
    private static PaymentRequest CardRequest(string cardNumber, string expiry = "12/34", string cvv = "123", decimal amount = 50m)
        => new("order-1", "user-customer", amount, "USD", "Card", cardNumber, "Asha Customer", expiry, cvv);

    [Fact]
    public void Approves_valid_card()
    {
        var result = PaymentGateway.Authorize(CardRequest("4242424242424242"), PaymentMethod.Card);

        Assert.True(result.Approved);
        Assert.Equal("**** **** **** 4242", result.MaskedInstrument);
    }

    [Fact]
    public void Declines_the_test_decline_card()
    {
        var result = PaymentGateway.Authorize(CardRequest("4000000000020000"), PaymentMethod.Card);

        Assert.False(result.Approved);
        Assert.Contains("declined", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_card_failing_luhn_check()
    {
        var result = PaymentGateway.Authorize(CardRequest("4242424242424241"), PaymentMethod.Card);

        Assert.False(result.Approved);
        Assert.Contains("checksum", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_expired_card()
    {
        var result = PaymentGateway.Authorize(CardRequest("4242424242424242", expiry: "01/20"), PaymentMethod.Card);

        Assert.False(result.Approved);
        Assert.Contains("expiry", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_invalid_cvv()
    {
        var result = PaymentGateway.Authorize(CardRequest("4242424242424242", cvv: "1"), PaymentMethod.Card);

        Assert.False(result.Approved);
        Assert.Contains("CVV", result.FailureReason);
    }

    [Fact]
    public void Rejects_non_positive_amount()
    {
        var result = PaymentGateway.Authorize(CardRequest("4242424242424242", amount: 0m), PaymentMethod.Card);

        Assert.False(result.Approved);
    }

    [Fact]
    public void Approves_non_card_methods_without_instrument_details()
    {
        var request = new PaymentRequest("order-1", "user-customer", 25m, "USD", "UPI", null, null, null, null);

        var result = PaymentGateway.Authorize(request, PaymentMethod.UPI);

        Assert.True(result.Approved);
        Assert.Equal("UPI account", result.MaskedInstrument);
    }
}
