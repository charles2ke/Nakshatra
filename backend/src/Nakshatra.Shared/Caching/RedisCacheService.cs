using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Nakshatra.Shared.Caching;

/// <summary>Redis backed cache used for product, vendor and user lookups.</summary>
public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await _redis.GetDatabase().StringGetAsync(key);
            return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value.ToString());
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis GET failed for {Key}; falling through to source of truth.", LogSanitizer.Sanitize(key));
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(value), expiry: ttl.HasValue ? new Expiration(ttl.Value) : default(Expiration));
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis SET failed for {Key}.", LogSanitizer.Sanitize(key));
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(key);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis DEL failed for {Key}.", LogSanitizer.Sanitize(key));
        }
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
