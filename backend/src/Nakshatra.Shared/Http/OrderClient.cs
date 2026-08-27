using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nakshatra.Shared.Models;

namespace Nakshatra.Shared.Http;

/// <summary>Client for the order service, used by the fulfillment and billing services.</summary>
public class OrderClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OrderClient> _logger;

    public OrderClient(HttpClient httpClient, IConfiguration configuration, ILogger<OrderClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(configuration["Services:Order"] ?? "http://localhost:5007");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<Order?> GetOrderAsync(string orderId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/orders/{orderId}", ct);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<Order>(ServiceDefaults.JsonOptions, ct)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Order service unavailable while loading order {OrderId}.", orderId);
            return null;
        }
    }

    public async Task UpdateStatusAsync(string orderId, string status, string note, CancellationToken ct = default)
    {
        try
        {
            await _httpClient.PostAsJsonAsync($"/api/orders/{orderId}/status", new { status, note }, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not update status for order {OrderId}.", orderId);
        }
    }
}
