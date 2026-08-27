using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Nakshatra.Shared;
using Nakshatra.Shared.Models;

namespace Nakshatra.Tests;

public class MediaServiceTests
{
    private static HttpClient CreateClient() =>
        new WebApplicationFactory<Nakshatra.Media.Service.ServiceMarker>().CreateClient();

    private static MultipartFormDataContent Upload(string contentType, string ownerType = "Product", string ownerId = "prod-headphones", string fileName = "photo.jpg")
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("fake-binary-content"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        return new MultipartFormDataContent
        {
            { file, "file", fileName },
            { new StringContent(ownerType), "ownerType" },
            { new StringContent(ownerId), "ownerId" }
        };
    }

    [Fact]
    public async Task Upload_accepts_an_image_and_queues_transcoding()
    {
        using var client = CreateClient();

        var response = await client.PostAsync("/api/media", Upload("image/jpeg"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var asset = await response.Content.ReadFromJsonAsync<MediaAsset>(ServiceDefaults.JsonOptions);
        Assert.Equal(MediaKind.Image, asset!.Kind);
        Assert.Equal("prod-headphones", asset.OwnerId);
        Assert.StartsWith(asset.Id, asset.StorageKey);
    }

    [Fact]
    public async Task Upload_transcodes_the_asset_into_renditions()
    {
        using var client = CreateClient();

        var response = await client.PostAsync("/api/media", Upload("image/png", fileName: "shot.png"));
        var asset = await response.Content.ReadFromJsonAsync<MediaAsset>(ServiceDefaults.JsonOptions);

        MediaAsset? processed = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            processed = await client.GetFromJsonAsync<MediaAsset>($"/api/media/{asset!.Id}", ServiceDefaults.JsonOptions);
            if (processed!.Status is MediaStatus.Ready or MediaStatus.Failed)
            {
                break;
            }

            await Task.Delay(100);
        }

        Assert.Equal(MediaStatus.Ready, processed!.Status);
        Assert.Contains(processed.Renditions, r => r.Name == "thumbnail");
        Assert.Contains(processed.Renditions, r => r.Name == "full");
    }

    [Fact]
    public async Task Upload_rejects_unsupported_media_types()
    {
        using var client = CreateClient();

        var response = await client.PostAsync("/api/media", Upload("application/x-msdownload", fileName: "malware.exe"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_an_unknown_owner_type()
    {
        using var client = CreateClient();

        var response = await client.PostAsync("/api/media", Upload("image/jpeg", ownerType: "Invoice"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ignores_client_supplied_paths_in_the_file_name()
    {
        using var client = CreateClient();

        var response = await client.PostAsync("/api/media", Upload("video/mp4", ownerType: "Review", ownerId: "review-1", fileName: "../../escape.mp4"));
        var asset = await response.Content.ReadFromJsonAsync<MediaAsset>(ServiceDefaults.JsonOptions);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("escape.mp4", asset!.OriginalFileName);
        Assert.DoesNotContain("..", asset.StorageKey);
        Assert.Equal(MediaKind.Video, asset.Kind);
    }

    [Fact]
    public async Task Assets_can_be_listed_by_owner()
    {
        using var client = CreateClient();
        await client.PostAsync("/api/media", Upload("image/jpeg", ownerId: "prod-lamp"));

        var assets = await client.GetFromJsonAsync<List<MediaAsset>>(
            "/api/media?ownerId=prod-lamp&ownerType=Product", ServiceDefaults.JsonOptions);

        Assert.NotEmpty(assets!);
        Assert.All(assets!, a => Assert.Equal("prod-lamp", a.OwnerId));
    }
}
