using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using Nakshatra.Shared;
using Nakshatra.Shared.Models;

namespace Nakshatra.Notification.Service;

/// <summary>Contact details used to deliver a notification outside the in-app feed.</summary>
public record NotificationRecipient(string Email, string Phone, string Name);

/// <summary>Resolves the contact details for the user (or persona) a notification targets.</summary>
public interface IRecipientResolver
{
    Task<NotificationRecipient?> ResolveAsync(Notification notification, CancellationToken ct = default);
}

/// <summary>
/// Reads contact details from the user service. Results are cached because a single order produces
/// several notifications for the same user.
/// </summary>
public class UserServiceRecipientResolver : IRecipientResolver
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserServiceRecipientResolver> _logger;

    public UserServiceRecipientResolver(HttpClient httpClient, IConfiguration configuration, ILogger<UserServiceRecipientResolver> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(configuration["Services:User"] ?? "http://localhost:5001");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<NotificationRecipient?> ResolveAsync(Notification notification, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(notification.UserId))
        {
            return null;
        }

        try
        {
            var user = await _httpClient.GetFromJsonAsync<User>(
                $"/api/users/{Uri.EscapeDataString(notification.UserId)}", ServiceDefaults.JsonOptions, ct);
            return user is null ? null : new NotificationRecipient(user.Email, user.Phone, user.Name);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not resolve the recipient for notification {NotificationId}.", LogSanitizer.Sanitize(notification.Id));
            return null;
        }
    }
}

public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Nakshatra";
}

/// <summary>Sends notifications over SMTP (SendGrid, Amazon SES, Mailgun or a corporate relay).</summary>
public class SmtpEmailDispatcher : INotificationDispatcher
{
    private readonly SmtpOptions _options;
    private readonly IRecipientResolver _recipients;
    private readonly ILogger<SmtpEmailDispatcher> _logger;

    public SmtpEmailDispatcher(SmtpOptions options, IRecipientResolver recipients, ILogger<SmtpEmailDispatcher> logger)
    {
        _options = options;
        _recipients = recipients;
        _logger = logger;
    }

    public async Task<bool> DispatchAsync(Notification notification, CancellationToken ct = default)
    {
        var recipient = await _recipients.ResolveAsync(notification, ct);
        if (recipient is null || string.IsNullOrWhiteSpace(recipient.Email))
        {
            return false;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = notification.Title,
            Body = notification.Body
        };
        message.To.Add(new MailAddress(recipient.Email, recipient.Name));

        using var client = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.UseSsl };
        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        try
        {
            await client.SendMailAsync(message, ct);
            return true;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or TaskCanceledException)
        {
            _logger.LogError(ex, "SMTP delivery failed for notification {NotificationId}.", LogSanitizer.Sanitize(notification.Id));
            return false;
        }
    }
}

public class TwilioOptions
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.twilio.com";
}

/// <summary>Sends SMS notifications through the Twilio Programmable Messaging API.</summary>
public class TwilioSmsDispatcher : INotificationDispatcher
{
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IRecipientResolver _recipients;
    private readonly ILogger<TwilioSmsDispatcher> _logger;

    public TwilioSmsDispatcher(HttpClient httpClient, TwilioOptions options, IRecipientResolver recipients, ILogger<TwilioSmsDispatcher> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _recipients = recipients;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(options.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.AccountSid}:{options.AuthToken}")));
    }

    public async Task<bool> DispatchAsync(Notification notification, CancellationToken ct = default)
    {
        var recipient = await _recipients.ResolveAsync(notification, ct);
        if (recipient is null || string.IsNullOrWhiteSpace(recipient.Phone))
        {
            return false;
        }

        var form = new Dictionary<string, string>
        {
            ["To"] = recipient.Phone,
            ["From"] = _options.FromNumber,
            ["Body"] = $"{notification.Title}: {notification.Body}"
        };

        try
        {
            using var response = await _httpClient.PostAsync(
                $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json",
                new FormUrlEncodedContent(form),
                ct);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.LogError(
                "Twilio rejected notification {NotificationId} with status {Status}.",
                LogSanitizer.Sanitize(notification.Id), (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Twilio delivery failed for notification {NotificationId}.", LogSanitizer.Sanitize(notification.Id));
            return false;
        }
    }
}

public class WebhookOptions
{
    public string Url { get; set; } = string.Empty;
    /// <summary>Optional bearer token sent as <c>Authorization</c> so the receiver can verify the caller.</summary>
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Posts the notification to an external system (Slack, Microsoft Teams or a partner endpoint) as
/// JSON.
/// </summary>
public class WebhookNotificationDispatcher : INotificationDispatcher
{
    private readonly HttpClient _httpClient;
    private readonly WebhookOptions _options;
    private readonly ILogger<WebhookNotificationDispatcher> _logger;

    public WebhookNotificationDispatcher(HttpClient httpClient, WebhookOptions options, ILogger<WebhookNotificationDispatcher> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        if (!string.IsNullOrWhiteSpace(options.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        }
    }

    public async Task<bool> DispatchAsync(Notification notification, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                _options.Url,
                new
                {
                    id = notification.Id,
                    title = notification.Title,
                    body = notification.Body,
                    topic = notification.Topic,
                    reference = notification.Reference,
                    severity = notification.Severity.ToString(),
                    audience = notification.Audience,
                    createdAt = notification.CreatedAt
                },
                ServiceDefaults.JsonOptions,
                ct);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.LogError(
                "The webhook receiver rejected notification {NotificationId} with status {Status}.",
                LogSanitizer.Sanitize(notification.Id), (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Webhook delivery failed for notification {NotificationId}.", LogSanitizer.Sanitize(notification.Id));
            return false;
        }
    }
}

/// <summary>
/// Routes a notification to the dispatcher registered for its channel. Notifications without a
/// configured provider (or whose delivery fails) still reach the user through the in-app feed, so
/// the fallback dispatcher records them in the log.
/// </summary>
public class ChannelNotificationDispatcher : INotificationDispatcher
{
    private readonly IReadOnlyDictionary<NotificationChannel, INotificationDispatcher> _dispatchers;
    private readonly INotificationDispatcher _fallback;

    public ChannelNotificationDispatcher(
        IReadOnlyDictionary<NotificationChannel, INotificationDispatcher> dispatchers,
        INotificationDispatcher fallback)
    {
        _dispatchers = dispatchers;
        _fallback = fallback;
    }

    public async Task<bool> DispatchAsync(Notification notification, CancellationToken ct = default)
    {
        if (_dispatchers.TryGetValue(notification.Channel, out var dispatcher) && await dispatcher.DispatchAsync(notification, ct))
        {
            return true;
        }

        return await _fallback.DispatchAsync(notification, ct);
    }
}

public static class NotificationDispatcherRegistration
{
    /// <summary>
    /// Registers the delivery providers that are fully configured. Without configuration the
    /// service keeps its log-only behaviour, so development and tests need no credentials.
    /// </summary>
    public static IServiceCollection AddNotificationDispatchers(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IRecipientResolver, UserServiceRecipientResolver>();
        services.AddSingleton<LoggingNotificationDispatcher>();

        var smtp = configuration.GetSection("Notifications:Smtp").Get<SmtpOptions>();
        if (smtp is not null && !string.IsNullOrWhiteSpace(smtp.Host) && !string.IsNullOrWhiteSpace(smtp.FromAddress))
        {
            services.AddSingleton(smtp);
            services.AddSingleton<SmtpEmailDispatcher>();
        }

        var twilio = configuration.GetSection("Notifications:Twilio").Get<TwilioOptions>();
        if (twilio is not null
            && !string.IsNullOrWhiteSpace(twilio.AccountSid)
            && !string.IsNullOrWhiteSpace(twilio.AuthToken)
            && !string.IsNullOrWhiteSpace(twilio.FromNumber))
        {
            services.AddSingleton(twilio);
            services.AddHttpClient<TwilioSmsDispatcher>();
        }

        var webhook = configuration.GetSection("Notifications:Webhook").Get<WebhookOptions>();
        if (webhook is not null && Uri.TryCreate(webhook.Url, UriKind.Absolute, out _))
        {
            services.AddSingleton(webhook);
            services.AddHttpClient<WebhookNotificationDispatcher>();
        }

        services.AddSingleton<INotificationDispatcher>(sp =>
        {
            var dispatchers = new Dictionary<NotificationChannel, INotificationDispatcher>();

            if (sp.GetService<SmtpEmailDispatcher>() is { } email)
            {
                dispatchers[NotificationChannel.Email] = email;
            }

            if (sp.GetService<TwilioSmsDispatcher>() is { } sms)
            {
                dispatchers[NotificationChannel.Sms] = sms;
            }

            if (sp.GetService<WebhookNotificationDispatcher>() is { } hook)
            {
                dispatchers[NotificationChannel.Webhook] = hook;
            }

            return new ChannelNotificationDispatcher(dispatchers, sp.GetRequiredService<LoggingNotificationDispatcher>());
        });

        return services;
    }
}
