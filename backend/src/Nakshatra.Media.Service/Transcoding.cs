using System.Diagnostics;
using Nakshatra.Shared.Models;

namespace Nakshatra.Media.Service;

public record RenditionSpec(string Name, string Format, int Width, int Height);

/// <summary>Produces derived renditions for an uploaded image or video.</summary>
public interface ITranscoder
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<MediaRendition>> TranscodeAsync(MediaAsset asset, string sourcePath, string outputDirectory, CancellationToken ct);
}

public static class RenditionProfiles
{
    // Images are transcoded to WebP; the ladder keeps thumbnails small for listing pages.
    public static readonly IReadOnlyList<RenditionSpec> Image = new List<RenditionSpec>
    {
        new("thumbnail", "webp", 240, 180),
        new("card", "webp", 480, 360),
        new("full", "webp", 1280, 960)
    };

    // Videos are transcoded to H.264/MP4 renditions for adaptive playback.
    public static readonly IReadOnlyList<RenditionSpec> Video = new List<RenditionSpec>
    {
        new("preview", "mp4", 480, 270),
        new("sd", "mp4", 854, 480),
        new("hd", "mp4", 1280, 720)
    };

    public static IReadOnlyList<RenditionSpec> For(MediaKind kind) => kind == MediaKind.Image ? Image : Video;
}

/// <summary>
/// Transcodes with the ffmpeg CLI. The container image ships ffmpeg; when it is missing the
/// service falls back to <see cref="PassthroughTranscoder"/>.
/// </summary>
public class FfmpegTranscoder : ITranscoder
{
    private readonly ILogger<FfmpegTranscoder> _logger;
    private readonly string _ffmpegPath;

    public FfmpegTranscoder(IConfiguration configuration, ILogger<FfmpegTranscoder> logger)
    {
        _logger = logger;
        _ffmpegPath = configuration["Media:FfmpegPath"] ?? "ffmpeg";
        IsAvailable = ProbeFfmpeg(_ffmpegPath);
    }

    public bool IsAvailable { get; }

    public async Task<IReadOnlyList<MediaRendition>> TranscodeAsync(MediaAsset asset, string sourcePath, string outputDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);
        var renditions = new List<MediaRendition>();

        foreach (var spec in RenditionProfiles.For(asset.Kind))
        {
            var outputPath = Path.Combine(outputDirectory, $"{spec.Name}.{spec.Format}");
            var arguments = asset.Kind == MediaKind.Image
                ? $"-y -i \"{sourcePath}\" -vf scale={spec.Width}:{spec.Height}:force_original_aspect_ratio=decrease -q:v 80 \"{outputPath}\""
                : $"-y -i \"{sourcePath}\" -vf scale={spec.Width}:{spec.Height}:force_original_aspect_ratio=decrease -c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart \"{outputPath}\"";

            if (!await RunAsync(arguments, ct))
            {
                _logger.LogWarning("ffmpeg failed to produce rendition {Rendition} for asset {AssetId}.", spec.Name, asset.Id);
                continue;
            }

            renditions.Add(new MediaRendition
            {
                Name = spec.Name,
                Format = spec.Format,
                Width = spec.Width,
                Height = spec.Height,
                SizeBytes = new FileInfo(outputPath).Length,
                Url = $"/api/media/{asset.Id}/renditions/{spec.Name}"
            });
        }

        return renditions;
    }

    private async Task<bool> RunAsync(string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        try
        {
            process.Start();
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogError(ex, "Could not run ffmpeg.");
            return false;
        }
    }

    private static bool ProbeFfmpeg(string ffmpegPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Fallback used when ffmpeg is unavailable: the original file is published as every rendition so
/// the portal keeps working, and the asset is flagged so operators can spot the degraded mode.
/// </summary>
public class PassthroughTranscoder : ITranscoder
{
    private readonly ILogger<PassthroughTranscoder> _logger;

    public PassthroughTranscoder(ILogger<PassthroughTranscoder> logger) => _logger = logger;

    public bool IsAvailable => true;

    public Task<IReadOnlyList<MediaRendition>> TranscodeAsync(MediaAsset asset, string sourcePath, string outputDirectory, CancellationToken ct)
    {
        _logger.LogWarning("ffmpeg is not available; serving the original file for asset {AssetId}.", asset.Id);
        Directory.CreateDirectory(outputDirectory);

        var size = new FileInfo(sourcePath).Length;
        var renditions = RenditionProfiles.For(asset.Kind)
            .Select(spec => new MediaRendition
            {
                Name = spec.Name,
                Format = Path.GetExtension(sourcePath).TrimStart('.'),
                Width = spec.Width,
                Height = spec.Height,
                SizeBytes = size,
                Url = $"/api/media/{asset.Id}/renditions/original"
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<MediaRendition>>(renditions);
    }
}
