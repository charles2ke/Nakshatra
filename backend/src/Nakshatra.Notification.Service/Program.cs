using Nakshatra.Notification.Service;
using Nakshatra.Shared;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddSingleton<INotificationDispatcher, LoggingNotificationDispatcher>();
builder.Services.AddHostedService<NotificationConsumer>();

var app = builder.Build();
app.UseNakshatraDefaults("notification-service");

app.MapGet("/api/notifications", async (
    string? userId,
    string? audience,
    bool? unreadOnly,
    IDocumentRepository<Notification> repo,
    CancellationToken ct) =>
{
    var notifications = await repo.ListAsync(ct);
    var filtered = notifications
        .Where(n => string.IsNullOrWhiteSpace(userId)
            ? string.IsNullOrWhiteSpace(audience) || n.Audience == audience
            : n.UserId == userId || (!string.IsNullOrWhiteSpace(audience) && n.Audience == audience))
        .Where(n => unreadOnly != true || !n.Read)
        .OrderByDescending(n => n.CreatedAt)
        .ToList();

    return Results.Ok(new
    {
        unreadCount = filtered.Count(n => !n.Read),
        items = filtered
    });
});

app.MapPost("/api/notifications/{id}/read", async (
    string id,
    IDocumentRepository<Notification> repo,
    CancellationToken ct) =>
{
    var notification = await repo.GetAsync(id, ct);
    if (notification is null)
    {
        return Results.NotFound();
    }

    notification.Read = true;
    await repo.UpsertAsync(notification, ct);
    return Results.Ok(notification);
});

app.MapPost("/api/notifications/read-all", async (
    MarkAllReadRequest request,
    IDocumentRepository<Notification> repo,
    CancellationToken ct) =>
{
    var notifications = await repo.ListAsync(ct);
    var targets = notifications
        .Where(n => !n.Read)
        .Where(n => (!string.IsNullOrWhiteSpace(request.UserId) && n.UserId == request.UserId)
            || (!string.IsNullOrWhiteSpace(request.Audience) && n.Audience == request.Audience))
        .ToList();

    foreach (var notification in targets)
    {
        notification.Read = true;
        await repo.UpsertAsync(notification, ct);
    }

    return Results.Ok(new { updated = targets.Count });
});

app.Run();

public record MarkAllReadRequest(string? UserId, string? Audience);
