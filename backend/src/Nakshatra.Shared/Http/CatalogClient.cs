using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nakshatra.Shared.Caching;
using Nakshatra.Shared.Models;

namespace Nakshatra.Shared.Http;

/// <summary>
/// Thin client used by the search, cart and recommendation services to read the product catalog.
/// Responses are cached in Redis to keep catalog fan-out cheap.
/// </summary>
public class CatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly ICacheService _cache;
    private readonly ILogger<CatalogClient> _logger;

    public CatalogClient(HttpClient httpClient, ICacheService cache, IConfiguration configuration, ILogger<CatalogClient> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(configuration["Services:Catalog"] ?? "http://localhost:5003");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<List<Product>>("catalog-client:products", ct);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var products = await _httpClient.GetFromJsonAsync<List<Product>>("/api/products", ServiceDefaults.JsonOptions, ct)
                           ?? new List<Product>();
            await _cache.SetAsync("catalog-client:products", products, TimeSpan.FromSeconds(60), ct);
            return products;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Catalog service unavailable; returning an empty product list.");
            return Array.Empty<Product>();
        }
    }

    public async Task<Product?> GetProductAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/products/{id}", ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<Product>(ServiceDefaults.JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Catalog service unavailable while loading product {ProductId}.", id);
            return null;
        }
    }

    public async Task ReserveStockAsync(string productId, int quantity, CancellationToken ct = default)
    {
        try
        {
            await _httpClient.PostAsJsonAsync($"/api/products/{productId}/reserve", new { quantity }, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not reserve stock for product {ProductId}.", productId);
        }
    }
}
