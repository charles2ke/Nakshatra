using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    /// <summary>Returns <c>null</c> when the order does not exist or the order service cannot be reached.</summary>
    public async Task<Order?> GetOrderAsync(string orderId, CancellationToken ct = default)
        => (await LookupOrderAsync(orderId, ct)).Order;

    /// <summary>Loads an order, telling a missing order apart from an unreachable order service.</summary>
    public async Task<OrderLookupResult> LookupOrderAsync(string orderId, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"/api/orders/{Uri.EscapeDataString(orderId)}", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new OrderLookupResult(OrderLookupStatus.NotFound);
            }

            var order = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<Order>(ServiceDefaults.JsonOptions, ct)
                : null;
            return order is null
                ? new OrderLookupResult(OrderLookupStatus.Unavailable)
                : new OrderLookupResult(OrderLookupStatus.Found, order);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Order service unavailable while loading order {OrderId}.", LogSanitizer.Sanitize(orderId));
            return new OrderLookupResult(OrderLookupStatus.Unavailable);
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

public enum OrderLookupStatus
{
    Found,
    NotFound,
    Unavailable
}

public record OrderLookupResult(OrderLookupStatus Status, Order? Order = null);
