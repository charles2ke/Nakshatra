using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nakshatra.Notification.Service;
using Nakshatra.Payment.Service;
using Nakshatra.Shared.Models;
using NotificationEntity = Nakshatra.Notification.Service.Notification;
using PaymentEntity = Nakshatra.Shared.Models.Payment;

namespace Nakshatra.Tests;

/// <summary>Records the outgoing request and replays a canned response, so no network is used.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _body;

    public StubHttpMessageHandler(HttpStatusCode statusCode, string body)
    {
        _statusCode = statusCode;
        _body = body;
    }

    public HttpRequestMessage? LastRequest { get; private set; }

    public string LastContent { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastContent = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json")
        };
    }
}

public class PaymentProviderTests
{
    private static (StripePaymentProvider Provider, StubHttpMessageHandler Handler) CreateStripe(HttpStatusCode status, string body)
    {
        var handler = new StubHttpMessageHandler(status, body);
        var provider = new StripePaymentProvider(
            new HttpClient(handler),
            new StripeOptions { SecretKey = "sk_test_key", BaseUrl = "https://api.stripe.test" },
            NullLogger<StripePaymentProvider>.Instance);
        return (provider, handler);
    }

    [Fact]
    public async Task Stripe_captures_a_succeeded_payment_intent()
    {
        var (provider, handler) = CreateStripe(
            HttpStatusCode.OK,
            """{"id":"pi_123","status":"succeeded","charges":{"data":[{"payment_method_details":{"card":{"last4":"4242"}}}]}}""");
        var request = new PaymentRequest("order-1", "user-1", 25.50m, "USD", "Card", null, null, null, null, "pm_card_visa");

        var result = await provider.AuthorizeAsync(request, PaymentMethod.Card);

        Assert.True(result.Approved);
        Assert.Equal("pi_123", result.ProviderReference);
        Assert.Equal("**** **** **** 4242", result.MaskedInstrument);
        Assert.Equal("/v1/payment_intents", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("amount=2550", handler.LastContent, StringComparison.Ordinal);
        Assert.Contains("currency=usd", handler.LastContent, StringComparison.Ordinal);
        Assert.Contains("payment_method=pm_card_visa", handler.LastContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stripe_surfaces_the_provider_error_message()
    {
        var (provider, _) = CreateStripe(
            HttpStatusCode.PaymentRequired,
            """{"error":{"message":"Your card was declined."}}""");
        var request = new PaymentRequest("order-1", "user-1", 25m, "USD", "Card", null, null, null, null, "pm_card_chargeDeclined");

        var result = await provider.AuthorizeAsync(request, PaymentMethod.Card);

        Assert.False(result.Approved);
        Assert.Equal("Your card was declined.", result.FailureReason);
    }

    [Fact]
    public async Task Stripe_requires_a_tokenized_instrument()
    {
        var (provider, handler) = CreateStripe(HttpStatusCode.OK, "{}");
        var request = new PaymentRequest("order-1", "user-1", 25m, "USD", "Card", "4242424242424242", "Asha", "12/34", "123");

        var result = await provider.AuthorizeAsync(request, PaymentMethod.Card);

        Assert.False(result.Approved);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Stripe_refunds_against_the_stored_payment_intent()
    {
        var (provider, handler) = CreateStripe(HttpStatusCode.OK, """{"id":"re_1","status":"succeeded"}""");

        var result = await provider.RefundAsync(new PaymentEntity { ProviderReference = "pi_123" });

        Assert.True(result.Succeeded);
        Assert.Equal("/v1/refunds", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("payment_intent=pi_123", handler.LastContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stripe_refund_fails_without_a_provider_reference()
    {
        var (provider, handler) = CreateStripe(HttpStatusCode.OK, "{}");

        var result = await provider.RefundAsync(new PaymentEntity());

        Assert.False(result.Succeeded);
        Assert.Null(handler.LastRequest);
    }

    [Theory]
    [InlineData(10.005, "usd", 1001L)]
    [InlineData(1500, "jpy", 1500L)]
    public void Amounts_are_converted_to_the_currency_minor_unit(decimal amount, string currency, long expected)
        => Assert.Equal(expected, StripePaymentProvider.ToMinorUnits(amount, currency));

    [Fact]
    public void Simulated_provider_is_used_when_stripe_credentials_are_missing()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddPaymentProvider(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Payments:Provider"] = "stripe" })
                .Build())
            .BuildServiceProvider();

        Assert.Equal("simulated", services.GetRequiredService<IPaymentProvider>().Name);
    }

    [Fact]
    public void Stripe_provider_is_used_when_credentials_are_configured()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddPaymentProvider(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Payments:Provider"] = "stripe",
                    ["Payments:Stripe:SecretKey"] = "sk_test_key"
                })
                .Build())
            .BuildServiceProvider();

        Assert.Equal("stripe", services.GetRequiredService<IPaymentProvider>().Name);
    }
}

public class NotificationDispatcherTests
{
    private sealed class StubRecipientResolver : IRecipientResolver
    {
        private readonly NotificationRecipient? _recipient;

        public StubRecipientResolver(NotificationRecipient? recipient) => _recipient = recipient;

        public Task<NotificationRecipient?> ResolveAsync(NotificationEntity notification, CancellationToken ct = default)
            => Task.FromResult(_recipient);
    }

    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        private readonly bool _result;

        public RecordingDispatcher(bool result) => _result = result;

        public int Calls { get; private set; }

        public Task<bool> DispatchAsync(NotificationEntity notification, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    [Fact]
    public async Task Twilio_posts_the_message_to_the_recipient_phone_number()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Created, """{"sid":"SM1"}""");
        var dispatcher = new TwilioSmsDispatcher(
            new HttpClient(handler),
            new TwilioOptions { AccountSid = "AC1", AuthToken = "token", FromNumber = "+15550000000", BaseUrl = "https://api.twilio.test" },
            new StubRecipientResolver(new NotificationRecipient("asha@example.com", "+15551234567", "Asha")),
            NullLogger<TwilioSmsDispatcher>.Instance);

        var delivered = await dispatcher.DispatchAsync(new NotificationEntity { Title = "Order received", Body = "Thanks!", Channel = NotificationChannel.Sms });

        Assert.True(delivered);
        Assert.Equal("/2010-04-01/Accounts/AC1/Messages.json", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("To=%2B15551234567", handler.LastContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Twilio_reports_failure_when_the_recipient_has_no_phone_number()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Created, "{}");
        var dispatcher = new TwilioSmsDispatcher(
            new HttpClient(handler),
            new TwilioOptions { AccountSid = "AC1", AuthToken = "token", FromNumber = "+15550000000", BaseUrl = "https://api.twilio.test" },
            new StubRecipientResolver(new NotificationRecipient("asha@example.com", string.Empty, "Asha")),
            NullLogger<TwilioSmsDispatcher>.Instance);

        Assert.False(await dispatcher.DispatchAsync(new NotificationEntity { Channel = NotificationChannel.Sms }));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Webhook_posts_the_notification_payload()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");
        var dispatcher = new WebhookNotificationDispatcher(
            new HttpClient(handler),
            new WebhookOptions { Url = "https://hooks.example.test/nakshatra", Token = "secret" },
            NullLogger<WebhookNotificationDispatcher>.Instance);

        var delivered = await dispatcher.DispatchAsync(new NotificationEntity { Title = "Media ready", Topic = "media.transcoded", Channel = NotificationChannel.Webhook });

        Assert.True(delivered);
        Assert.Equal("https://hooks.example.test/nakshatra", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("\"title\":\"Media ready\"", handler.LastContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Channel_dispatcher_routes_to_the_provider_for_the_channel()
    {
        var email = new RecordingDispatcher(true);
        var fallback = new RecordingDispatcher(true);
        var dispatcher = new ChannelNotificationDispatcher(
            new Dictionary<NotificationChannel, INotificationDispatcher> { [NotificationChannel.Email] = email },
            fallback);

        Assert.True(await dispatcher.DispatchAsync(new NotificationEntity { Channel = NotificationChannel.Email }));
        Assert.Equal(1, email.Calls);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task Channel_dispatcher_falls_back_when_the_provider_cannot_deliver()
    {
        var email = new RecordingDispatcher(false);
        var fallback = new RecordingDispatcher(true);
        var dispatcher = new ChannelNotificationDispatcher(
            new Dictionary<NotificationChannel, INotificationDispatcher> { [NotificationChannel.Email] = email },
            fallback);

        Assert.True(await dispatcher.DispatchAsync(new NotificationEntity { Channel = NotificationChannel.Email }));
        Assert.Equal(1, email.Calls);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task Channel_dispatcher_falls_back_for_unconfigured_channels()
    {
        var fallback = new RecordingDispatcher(true);
        var dispatcher = new ChannelNotificationDispatcher(
            new Dictionary<NotificationChannel, INotificationDispatcher>(),
            fallback);

        Assert.True(await dispatcher.DispatchAsync(new NotificationEntity { Channel = NotificationChannel.Sms }));
        Assert.Equal(1, fallback.Calls);
    }
}
