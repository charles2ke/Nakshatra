using Nakshatra.Shared.Models;

namespace Nakshatra.Notification.Service;

public enum NotificationChannel
{
    InApp,
    Email,
    Sms,
    Webhook
}

public enum NotificationSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>A notification raised from a domain event and delivered to a user or role.</summary>
public class Notification : IEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    /// <summary>Set when the notification targets a persona (e.g. all fulfillment agents).</summary>
    public string Audience { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public bool Read { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
