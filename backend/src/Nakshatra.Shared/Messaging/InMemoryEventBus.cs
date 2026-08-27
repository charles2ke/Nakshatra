using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Nakshatra.Shared.Messaging;

/// <summary>
/// Event bus used when Kafka is not configured. Events are dispatched to in-process handlers and,
/// optionally, fanned out over HTTP to the services listed in <c>EventFanout:Endpoints</c> so the
/// end-to-end flow still works when running the services without Kafka.
/// </summary>
public class InMemoryEventBus : IEventPublisher, IEventSubscriber
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<Func<EventEnvelope, CancellationToken, Task>>> Handlers = new();

    private readonly ILogger<InMemoryEventBus> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string[] _fanoutEndpoints;
    private readonly string? _internalToken;

    public InMemoryEventBus(ILogger<InMemoryEventBus> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _fanoutEndpoints = configuration.GetSection("EventFanout:Endpoints").Get<string[]>() ?? Array.Empty<string>();
        _internalToken = configuration["Internal:Token"];
    }

    public async Task PublishAsync<T>(string topic, string key, T payload, CancellationToken ct = default)
    {
        var envelope = new EventEnvelope(topic, key, JsonSerializer.Serialize(payload));
        await DispatchAsync(envelope, ct);

        foreach (var endpoint in _fanoutEndpoints)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                if (!string.IsNullOrWhiteSpace(_internalToken))
                {
                    client.DefaultRequestHeaders.Add("X-Internal-Token", _internalToken);
                }

                await client.PostAsJsonAsync($"{endpoint.TrimEnd('/')}/internal/events", envelope, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Event fan-out to {Endpoint} failed for topic {Topic}.", endpoint, topic);
            }
        }
    }

    /// <summary>Delivers an envelope to every handler subscribed in this process.</summary>
    public async Task DispatchAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        if (!Handlers.TryGetValue(envelope.Topic, out var handlers))
        {
            return;
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler(envelope, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handler for topic {Topic} failed.", envelope.Topic);
            }
        }
    }

    public Task SubscribeAsync(string topic, string consumerGroup, Func<EventEnvelope, CancellationToken, Task> handler, CancellationToken ct)
    {
        Handlers.GetOrAdd(topic, _ => new ConcurrentBag<Func<EventEnvelope, CancellationToken, Task>>()).Add(handler);
        return Task.CompletedTask;
    }
}
