using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nakshatra.Shared.Models;

namespace Nakshatra.Shared.Http;

/// <summary>Client for the recommendation service used by the cart service.</summary>
public class RecommendationClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RecommendationClient> _logger;

    public RecommendationClient(HttpClient httpClient, IConfiguration configuration, ILogger<RecommendationClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(configuration["Services:Recommendation"] ?? "http://localhost:5006");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<IReadOnlyList<Product>> GetAlsoPurchasedAsync(string productId, int limit = 4, CancellationToken ct = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<Product>>(
                       $"/api/recommendations/also-purchased/{productId}?limit={limit}", ServiceDefaults.JsonOptions, ct)
                   ?? new List<Product>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Recommendation service unavailable for product {ProductId}.", productId);
            return Array.Empty<Product>();
        }
    }
}
