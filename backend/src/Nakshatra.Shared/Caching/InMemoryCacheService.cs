using System.Collections.Concurrent;
using System.Text.Json;

namespace Nakshatra.Shared.Caching;

/// <summary>In-process cache used when Redis is not configured.</summary>
public class InMemoryCacheService : ICacheService
{
    private static readonly ConcurrentDictionary<string, (string Payload, DateTime? ExpiresAt)> Entries = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (Entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt is null || entry.ExpiresAt > DateTime.UtcNow)
            {
                return Task.FromResult(JsonSerializer.Deserialize<T>(entry.Payload));
            }

            Entries.TryRemove(key, out _);
        }

        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        Entries[key] = (JsonSerializer.Serialize(value), ttl is null ? null : DateTime.UtcNow.Add(ttl.Value));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        Entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null)
        {
            return cached;
        }

        var value = await factory();
        if (value is not null)
        {
            await SetAsync(key, value, ttl, ct);
        }

        return value;
    }
}
