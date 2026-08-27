using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nakshatra.Shared.Caching;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Shared.Endpoints;

/// <summary>
/// Read-through/write-through CRUD endpoints: reads are served from Redis when warm and the
/// cache entries are invalidated on every write.
/// </summary>
public static class CachedCrudEndpoints
{
    public static RouteGroupBuilder MapCachedCrud<T>(
        this IEndpointRouteBuilder app,
        string route,
        string cachePrefix,
        TimeSpan? ttl = null,
        Action<T>? onCreate = null)
        where T : class, IEntity
    {
        var cacheTtl = ttl ?? TimeSpan.FromMinutes(10);
        var group = app.MapGroup(route);
        var listKey = $"{cachePrefix}:all";

        group.MapGet("/", async (IDocumentRepository<T> repo, ICacheService cache) =>
        {
            var items = await cache.GetOrSetAsync(listKey, async () => (await repo.ListAsync()).ToList(), cacheTtl);
            return Results.Ok(items);
        });

        group.MapGet("/{id}", async (string id, IDocumentRepository<T> repo, ICacheService cache) =>
        {
            var item = await cache.GetOrSetAsync($"{cachePrefix}:{id}", () => repo.GetAsync(id), cacheTtl);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPost("/", async (T entity, IDocumentRepository<T> repo, ICacheService cache) =>
        {
            if (string.IsNullOrWhiteSpace(entity.Id))
            {
                entity.Id = Guid.NewGuid().ToString("N");
            }

            onCreate?.Invoke(entity);
            var saved = await repo.UpsertAsync(entity);
            await cache.RemoveAsync(listKey);
            await cache.SetAsync($"{cachePrefix}:{saved.Id}", saved, cacheTtl);
            return Results.Created($"{route}/{saved.Id}", saved);
        });

        group.MapPut("/{id}", async (string id, T entity, IDocumentRepository<T> repo, ICacheService cache) =>
        {
            var existing = await repo.GetAsync(id);
            if (existing is null)
            {
                return Results.NotFound();
            }

            entity.Id = id;
            var saved = await repo.UpsertAsync(entity);
            await cache.RemoveAsync(listKey);
            await cache.SetAsync($"{cachePrefix}:{id}", saved, cacheTtl);
            return Results.Ok(saved);
        });

        group.MapDelete("/{id}", async (string id, IDocumentRepository<T> repo, ICacheService cache) =>
        {
            var deleted = await repo.DeleteAsync(id);
            await cache.RemoveAsync(listKey);
            await cache.RemoveAsync($"{cachePrefix}:{id}");
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        return group;
    }
}
