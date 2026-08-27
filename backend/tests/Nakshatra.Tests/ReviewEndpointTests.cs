using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nakshatra.Shared;
using Nakshatra.Shared.Models;

namespace Nakshatra.Tests;

public class ReviewEndpointTests
{
    private static HttpClient CreateClient() =>
        new WebApplicationFactory<Nakshatra.Catalog.Service.ServiceMarker>().CreateClient();

    [Fact]
    public async Task Creates_and_lists_a_review_with_media()
    {
        using var client = CreateClient();

        var created = await client.PostAsJsonAsync("/api/products/prod-lamp/reviews", new
        {
            userId = "user-customer",
            title = "Great lamp",
            body = "Bright and quiet.",
            rating = 5,
            mediaIds = new[] { "media-1" }
        }, ServiceDefaults.JsonOptions);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var reviews = await client.GetFromJsonAsync<List<Review>>("/api/products/prod-lamp/reviews", ServiceDefaults.JsonOptions);

        Assert.Contains(reviews!, r => r.Title == "Great lamp" && r.MediaIds.Contains("media-1"));
    }

    [Fact]
    public async Task Rejects_an_out_of_range_rating()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/products/prod-lamp/reviews", new
        {
            userId = "user-customer",
            rating = 9
        }, ServiceDefaults.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_review_for_an_unknown_product()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/products/does-not-exist/reviews", new
        {
            userId = "user-customer",
            rating = 4
        }, ServiceDefaults.JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
