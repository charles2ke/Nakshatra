using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nakshatra.Shared;
using Nakshatra.Shared.Caching;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using OrderEntity = Nakshatra.Shared.Models.Order;

namespace Nakshatra.Tests;

/// <summary>
/// Builds in-process service hosts whose service-to-service clients call other in-process hosts or
/// stubs instead of the network.
/// </summary>
internal static class TestHosts
{
    public static WebApplicationFactory<TMarker> Create<TMarker>(Action<IServiceCollection>? configure = null) where TMarker : class
        => new WebApplicationFactory<TMarker>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // appsettings.json fans events out to localhost ports; keep test events inside the test process.
            services.AddSingleton(sp => new InMemoryEventBus(
                sp.GetRequiredService<ILogger<InMemoryEventBus>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                new ConfigurationBuilder().Build()));
            configure?.Invoke(services);
        }));

    /// <summary>Sends a typed client's requests to the given handler, e.g. another in-process host.</summary>
    public static void Route<TClient>(this IServiceCollection services, Func<HttpMessageHandler> handler) where TClient : class
        => services.AddHttpClient<TClient>().ConfigurePrimaryHttpMessageHandler(handler);

    /// <summary>A payment service whose order lookups return <paramref name="order"/>.</summary>
    public static WebApplicationFactory<Nakshatra.Payment.Service.ServiceMarker> PaymentServiceFor(
        OrderEntity order, HttpStatusCode lookupStatus = HttpStatusCode.OK)
        => Create<Nakshatra.Payment.Service.ServiceMarker>(services =>
            services.Route<OrderClient>(() => new StubHttpMessageHandler(lookupStatus, JsonSerializer.Serialize(order, ServiceDefaults.JsonOptions))));

    public static OrderEntity AwaitingPayment(decimal total) => new()
    {
        UserId = $"user-{Guid.NewGuid():N}",
        Subtotal = total,
        Total = total,
        Status = OrderStatus.AwaitingPayment
    };

    /// <summary>JSON response in the services' wire format.</summary>
    public static HttpResponseMessage Json<T>(T body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, ServiceDefaults.JsonOptions), Encoding.UTF8, "application/json")
    };
}

/// <summary>Answers each request with a response chosen by the test, e.g. by path.</summary>
internal sealed class LambdaHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(respond(request));
}

/// <summary>
/// Always calls through. The in-process cache is static, so a cached catalog listing from another test
/// would otherwise hide the products a test set up.
/// </summary>
internal sealed class PassThroughCache : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(default(T));

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

    public Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default) => factory();
}

/// <summary>Records published events instead of delivering them.</summary>
internal sealed class CapturingPublisher : IEventPublisher
{
    private readonly ConcurrentQueue<(string Topic, string Key, JsonElement Payload)> _events = new();

    public IReadOnlyList<(string Topic, string Key, JsonElement Payload)> Events => _events.ToList();

    public Task PublishAsync<T>(string topic, string key, T payload, CancellationToken ct = default)
    {
        _events.Enqueue((topic, key, JsonSerializer.SerializeToElement(payload)));
        return Task.CompletedTask;
    }
}
