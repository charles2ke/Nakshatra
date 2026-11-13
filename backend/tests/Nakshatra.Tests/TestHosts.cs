using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nakshatra.Shared;
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
}
