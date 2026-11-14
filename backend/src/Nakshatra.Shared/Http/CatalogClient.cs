using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        => (await LookupProductAsync(id, ct)).Product;

    public async Task<ProductLookupResult> LookupProductAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/products/{id}", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ProductLookupResult(ProductLookupStatus.NotFound);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ProductLookupResult(ProductLookupStatus.Unavailable);
            }

            var product = await response.Content.ReadFromJsonAsync<Product>(ServiceDefaults.JsonOptions, ct);
            return product is null
                ? new ProductLookupResult(ProductLookupStatus.Unavailable)
                : new ProductLookupResult(ProductLookupStatus.Found, product);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Catalog service unavailable while loading product {ProductId}.", id);
            return new ProductLookupResult(ProductLookupStatus.Unavailable);
        }
    }

    /// <summary>Takes stock out of the catalog. Callers must release it again if the order is not placed.</summary>
    public async Task<StockReservationResult> ReserveStockAsync(string productId, int quantity, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync($"/api/products/{Uri.EscapeDataString(productId)}/reserve", new { quantity }, ct);
            if (response.IsSuccessStatusCode)
            {
                return new StockReservationResult(StockReservationStatus.Reserved);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Conflict => new StockReservationResult(StockReservationStatus.InsufficientStock, await ReadAvailableAsync(response, ct)),
                HttpStatusCode.NotFound => new StockReservationResult(StockReservationStatus.ProductNotFound),
                _ => new StockReservationResult(StockReservationStatus.Failed)
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not reserve stock for product {ProductId}.", LogSanitizer.Sanitize(productId));
            return new StockReservationResult(StockReservationStatus.Failed);
        }
    }

    /// <summary>Returns previously reserved stock to the catalog, e.g. when an order is cancelled.</summary>
    public async Task<bool> ReleaseStockAsync(string productId, int quantity, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync($"/api/products/{Uri.EscapeDataString(productId)}/release", new { quantity }, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Catalog refused to release {Quantity} unit(s) of product {ProductId}: HTTP {StatusCode}.",
                    quantity, LogSanitizer.Sanitize(productId), (int)response.StatusCode);
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not release {Quantity} unit(s) of product {ProductId}.", quantity, LogSanitizer.Sanitize(productId));
            return false;
        }
    }

    /// <summary>Releases every line, ignoring the caller's cancellation so stock is not stranded.</summary>
    public async Task ReleaseStockAsync(IEnumerable<CartItem> lines)
    {
        foreach (var line in lines)
        {
            await ReleaseStockAsync(line.ProductId, line.Quantity, CancellationToken.None);
        }
    }

    private static async Task<int> ReadAvailableAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var conflict = await response.Content.ReadFromJsonAsync<StockConflict>(ServiceDefaults.JsonOptions, ct);
            return conflict?.Available ?? 0;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return 0;
        }
    }

    private sealed record StockConflict(int Available);
}

public enum StockReservationStatus
{
    Reserved,
    InsufficientStock,
    ProductNotFound,
    Failed
}

public record StockReservationResult(StockReservationStatus Status, int Available = 0)
{
    public bool Succeeded => Status == StockReservationStatus.Reserved;
}

public enum ProductLookupStatus
{
    Found,
    NotFound,
    Unavailable
}

public record ProductLookupResult(ProductLookupStatus Status, Product? Product = null);
