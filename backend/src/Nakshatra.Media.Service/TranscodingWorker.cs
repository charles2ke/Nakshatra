using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Media.Service;

/// <summary>
/// Consumes <c>media.uploaded</c> and transcodes the asset into the rendition ladder. Because it
/// runs as a Kafka consumer group, transcoding work is spread across every replica.
/// </summary>
public class TranscodingWorker : EventConsumerBackgroundService
{
    public TranscodingWorker(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<TranscodingWorker> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.MediaUploaded;

    protected override string ConsumerGroup => "media-transcoder";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var uploaded = envelope.As<MediaAsset>();
        if (uploaded is null)
        {
            return;
        }

        var repo = scopedServices.GetRequiredService<IDocumentRepository<MediaAsset>>();
        var storage = scopedServices.GetRequiredService<MediaStorage>();
        var transcoder = scopedServices.GetRequiredService<ITranscoder>();
        var publisher = scopedServices.GetRequiredService<IEventPublisher>();
        var logger = scopedServices.GetRequiredService<ILogger<TranscodingWorker>>();

        var asset = await repo.GetAsync(uploaded.Id, ct);
        if (asset is null || asset.Status is MediaStatus.Ready or MediaStatus.Processing)
        {
            return;
        }

        asset.Status = MediaStatus.Processing;
        asset.UpdatedAt = DateTime.UtcNow;
        await repo.UpsertAsync(asset, ct);

        try
        {
            var sourcePath = storage.Resolve(asset.StorageKey);
            var renditions = await transcoder.TranscodeAsync(asset, sourcePath, storage.RenditionDirectory(asset.Id), ct);

            asset.Renditions = renditions.ToList();
            asset.Status = renditions.Count > 0 ? MediaStatus.Ready : MediaStatus.Failed;
            asset.Error = renditions.Count > 0 ? string.Empty : "No renditions could be produced.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Transcoding failed for asset {AssetId}.", asset.Id);
            asset.Status = MediaStatus.Failed;
            asset.Error = "Transcoding failed.";
        }

        asset.UpdatedAt = DateTime.UtcNow;
        await repo.UpsertAsync(asset, ct);
        await publisher.PublishAsync(Topics.MediaTranscoded, asset.Id, asset, ct);
    }
}
