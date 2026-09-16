using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nakshatra.Shared;
using Nakshatra.Shared.Models;

namespace Nakshatra.Payment.Service;

public record RefundResult(bool Succeeded, string FailureReason);

/// <summary>
/// A payment service provider. Implementations either talk to a real acquirer or simulate one, so
/// the endpoints stay identical whether or not provider credentials are configured.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Provider name recorded on the payment (for example <c>simulated</c> or <c>stripe</c>).</summary>
    string Name { get; }

    Task<GatewayResult> AuthorizeAsync(PaymentRequest request, PaymentMethod method, CancellationToken ct = default);

    Task<RefundResult> RefundAsync(Shared.Models.Payment payment, CancellationToken ct = default);
}

/// <summary>Wraps the built-in simulated gateway so it can be used through <see cref="IPaymentProvider"/>.</summary>
public class SimulatedPaymentProvider : IPaymentProvider
{
    public string Name => "simulated";

    public Task<GatewayResult> AuthorizeAsync(PaymentRequest request, PaymentMethod method, CancellationToken ct = default)
        => Task.FromResult(PaymentGateway.Authorize(request, method));

    public Task<RefundResult> RefundAsync(Shared.Models.Payment payment, CancellationToken ct = default)
        => Task.FromResult(new RefundResult(true, string.Empty));
}

public class StripeOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.stripe.com";
}

/// <summary>
/// Stripe integration built on the PaymentIntents API. The portal collects the instrument with
/// Stripe.js and sends the resulting payment method token, so raw card data never reaches this
/// service; requests that still carry a PAN are rejected.
/// </summary>
public class StripePaymentProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StripePaymentProvider> _logger;

    public StripePaymentProvider(HttpClient httpClient, StripeOptions options, ILogger<StripePaymentProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(options.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.SecretKey);
    }

    public string Name => "stripe";

    public async Task<GatewayResult> AuthorizeAsync(PaymentRequest request, PaymentMethod method, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            return new GatewayResult(false, string.Empty, "Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.PaymentMethodToken))
        {
            return new GatewayResult(false, string.Empty, "A payment method token from Stripe.js is required.");
        }

        if (!string.IsNullOrWhiteSpace(request.CardNumber))
        {
            return new GatewayResult(false, string.Empty, "Raw card numbers are not accepted; submit a Stripe.js payment method token instead.");
        }

        var currency = (string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency).ToLowerInvariant();
        var form = new Dictionary<string, string>
        {
            ["amount"] = ToMinorUnits(request.Amount, currency).ToString(CultureInfo.InvariantCulture),
            ["currency"] = currency,
            ["payment_method"] = request.PaymentMethodToken,
            ["confirm"] = "true",
            ["off_session"] = "true",
            ["metadata[orderId]"] = request.OrderId,
            ["metadata[userId]"] = request.UserId
        };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/payment_intents")
            {
                Content = new FormUrlEncodedContent(form)
            };
            httpRequest.Headers.Add("Idempotency-Key", ComputeIdempotencyKey(request));

            using var response = await _httpClient.SendAsync(httpRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!response.IsSuccessStatusCode)
            {
                return new GatewayResult(false, string.Empty, ReadError(root));
            }

            var status = root.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : null;
            var reference = root.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty;
            var masked = ReadInstrument(root, method);

            return status is "succeeded" or "requires_capture"
                ? new GatewayResult(true, masked, string.Empty, reference)
                : new GatewayResult(false, masked, $"Stripe returned status '{status}'.", reference);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "Stripe authorization failed for order {OrderId}.", LogSanitizer.Sanitize(request.OrderId));
            return new GatewayResult(false, string.Empty, "The payment provider is unavailable. Please try again.");
        }
    }

    public async Task<RefundResult> RefundAsync(Shared.Models.Payment payment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(payment.ProviderReference))
        {
            return new RefundResult(false, "The payment has no provider reference to refund.");
        }

        var form = new Dictionary<string, string>
        {
            ["payment_intent"] = payment.ProviderReference
        };

        try
        {
            using var response = await _httpClient.PostAsync("/v1/refunds", new FormUrlEncodedContent(form), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);

            if (!response.IsSuccessStatusCode)
            {
                return new RefundResult(false, ReadError(document.RootElement));
            }

            var status = document.RootElement.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : null;
            return status switch
            {
                "succeeded" => new RefundResult(true, string.Empty),
                "pending" => new RefundResult(false, "The refund is pending and has not completed yet."),
                "failed" => new RefundResult(false, "The refund failed."),
                "canceled" => new RefundResult(false, "The refund was canceled."),
                _ => new RefundResult(false, $"Stripe returned refund status '{status}'.")
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "Stripe refund failed for payment {PaymentId}.", LogSanitizer.Sanitize(payment.Id));
            return new RefundResult(false, "The payment provider is unavailable. Please try again.");
        }
    }

    /// <summary>
    /// Derives a stable idempotency key from the attempt's identifying parameters, so a network
    /// retry of the exact same charge reuses the same key (Stripe returns the original result
    /// instead of creating a second PaymentIntent) while a genuinely new attempt - a different
    /// amount or instrument - gets its own key.
    /// </summary>
    private static string ComputeIdempotencyKey(PaymentRequest request)
    {
        var payload = string.Join(
            '|',
            request.OrderId,
            request.Amount.ToString(CultureInfo.InvariantCulture),
            request.Currency ?? string.Empty,
            request.PaymentMethodToken ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    /// <summary>Currencies without a minor unit (for example JPY) are sent as whole units.</summary>
    public static long ToMinorUnits(decimal amount, string currency)
    {
        string[] zeroDecimalCurrencies = { "bif", "clp", "djf", "gnf", "jpy", "kmf", "krw", "mga", "pyg", "rwf", "ugx", "vnd", "vuv", "xaf", "xof", "xpf" };
        return zeroDecimalCurrencies.Contains(currency)
            ? (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero)
            : (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
    }

    private static string ReadError(JsonElement root)
        => root.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
            ? message.GetString() ?? "The payment was declined."
            : "The payment was declined.";

    private static string ReadInstrument(JsonElement root, PaymentMethod method)
    {
        if (root.TryGetProperty("charges", out var charges)
            && charges.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array
            && data.GetArrayLength() > 0
            && data[0].TryGetProperty("payment_method_details", out var details)
            && details.TryGetProperty("card", out var card)
            && card.TryGetProperty("last4", out var last4)
            && last4.GetString() is { Length: 4 } digits)
        {
            return $"**** **** **** {digits}";
        }

        return method == PaymentMethod.Card ? "Card on file" : $"{method} account";
    }
}

public static class PaymentProviderRegistration
{
    /// <summary>
    /// Registers the configured payment provider. <c>Payments:Provider</c> selects the integration;
    /// anything other than a fully configured provider falls back to the simulated gateway so the
    /// service still runs in development.
    /// </summary>
    public static IServiceCollection AddPaymentProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Payments:Provider"];
        var secretKey = configuration["Payments:Stripe:SecretKey"];

        if (string.Equals(provider, "stripe", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(secretKey))
        {
            var options = new StripeOptions
            {
                SecretKey = secretKey,
                BaseUrl = configuration["Payments:Stripe:BaseUrl"] ?? "https://api.stripe.com"
            };
            services.AddSingleton(options);
            services.AddHttpClient<IPaymentProvider, StripePaymentProvider>();
            return services;
        }

        if (!string.IsNullOrWhiteSpace(provider) && !string.Equals(provider, "simulated", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPaymentProvider>(sp =>
            {
                sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Nakshatra.Payments")
                    .LogWarning(
                        "Payment provider '{Provider}' is not configured (missing credentials); falling back to the simulated gateway.",
                        LogSanitizer.Sanitize(provider));
                return new SimulatedPaymentProvider();
            });
            return services;
        }

        services.AddSingleton<IPaymentProvider, SimulatedPaymentProvider>();
        return services;
    }
}
