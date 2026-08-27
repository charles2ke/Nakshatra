using Nakshatra.Media.Service;
using Nakshatra.Shared;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddSingleton<MediaStorage>();
builder.Services.AddSingleton<FfmpegTranscoder>();
builder.Services.AddSingleton<PassthroughTranscoder>();
builder.Services.AddSingleton<ITranscoder>(sp =>
{
    var ffmpeg = sp.GetRequiredService<FfmpegTranscoder>();
    return ffmpeg.IsAvailable ? ffmpeg : sp.GetRequiredService<PassthroughTranscoder>();
});
builder.Services.AddHostedService<TranscodingWorker>();

var maxUploadBytes = builder.Configuration.GetValue<long?>("Media:MaxUploadBytes") ?? 200L * 1024 * 1024;

var app = builder.Build();
app.UseNakshatraDefaults("media-service");

// Uploading product or review media kicks off transcoding asynchronously via Kafka.
app.MapPost("/api/media", async (
    HttpRequest request,
    IDocumentRepository<MediaAsset> repo,
    MediaStorage storage,
    IEventPublisher publisher,
    CancellationToken ct) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "A multipart/form-data upload is required." });
    }

    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "A non-empty 'file' part is required." });
    }

    if (file.Length > maxUploadBytes)
    {
        return Results.BadRequest(new { error = $"Uploads are limited to {maxUploadBytes} bytes." });
    }

    if (!Enum.TryParse<MediaOwnerType>(form["ownerType"], ignoreCase: true, out var ownerType))
    {
        return Results.BadRequest(new { error = "ownerType must be 'Product' or 'Review'." });
    }

    var ownerId = form["ownerId"].ToString();
    if (string.IsNullOrWhiteSpace(ownerId))
    {
        return Results.BadRequest(new { error = "ownerId is required." });
    }

    if (!MediaContentTypes.TryResolve(file.ContentType, out var kind, out var extension))
    {
        return Results.BadRequest(new
        {
            error = "Unsupported media type.",
            supported = MediaContentTypes.Supported
        });
    }

    var asset = new MediaAsset
    {
        OwnerType = ownerType,
        OwnerId = ownerId,
        // The client supplied name is only kept for display; it never influences the storage path.
        OriginalFileName = Path.GetFileName(file.FileName),
        ContentType = file.ContentType,
        Kind = kind,
        SizeBytes = file.Length
    };

    var originalPath = storage.OriginalPath(asset.Id, extension);
    await using (var stream = file.OpenReadStream())
    {
        await storage.SaveAsync(originalPath, stream, ct);
    }

    asset.StorageKey = Path.Combine(asset.Id, $"original{extension}");
    await repo.UpsertAsync(asset, ct);
    await publisher.PublishAsync(Topics.MediaUploaded, asset.Id, asset, ct);

    return Results.Accepted($"/api/media/{asset.Id}", asset);
});

app.MapGet("/api/media/{id}", async (string id, IDocumentRepository<MediaAsset> repo, CancellationToken ct) =>
    await repo.GetAsync(id, ct) is { } asset ? Results.Ok(asset) : Results.NotFound());

app.MapGet("/api/media", async (
    string? ownerId,
    string? ownerType,
    IDocumentRepository<MediaAsset> repo,
    CancellationToken ct) =>
{
    var assets = await repo.ListAsync(ct);
    var filtered = assets
        .Where(a => string.IsNullOrWhiteSpace(ownerId) || a.OwnerId == ownerId)
        .Where(a => string.IsNullOrWhiteSpace(ownerType)
                    || string.Equals(a.OwnerType.ToString(), ownerType, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(a => a.CreatedAt)
        .ToList();

    return Results.Ok(filtered);
});

// Streams a transcoded rendition (or the original when transcoding is degraded).
app.MapGet("/api/media/{id}/renditions/{rendition}", async (
    string id,
    string rendition,
    IDocumentRepository<MediaAsset> repo,
    MediaStorage storage,
    CancellationToken ct) =>
{
    var asset = await repo.GetAsync(id, ct);
    if (asset is null)
    {
        return Results.NotFound();
    }

    if (string.Equals(rendition, "original", StringComparison.OrdinalIgnoreCase))
    {
        var path = storage.Resolve(asset.StorageKey);
        return File.Exists(path)
            ? Results.File(path, asset.ContentType)
            : Results.NotFound();
    }

    var match = asset.Renditions.FirstOrDefault(r => string.Equals(r.Name, rendition, StringComparison.OrdinalIgnoreCase));
    if (match is null)
    {
        return Results.NotFound();
    }

    var renditionPath = storage.RenditionPath(asset.Id, match.Name, match.Format);
    return File.Exists(renditionPath)
        ? Results.File(renditionPath, MediaContentTypes.ContentTypeFor(match.Format))
        : Results.NotFound();
});

app.MapDelete("/api/media/{id}", async (string id, IDocumentRepository<MediaAsset> repo, MediaStorage storage, CancellationToken ct) =>
{
    var asset = await repo.GetAsync(id, ct);
    if (asset is null)
    {
        return Results.NotFound();
    }

    var directory = storage.Resolve(asset.Id);
    if (Directory.Exists(directory))
    {
        Directory.Delete(directory, recursive: true);
    }

    await repo.DeleteAsync(id, ct);
    return Results.NoContent();
});

app.Run();

public partial class Program;
