using System.Collections.Concurrent;
using System.Linq.Expressions;
using Nakshatra.Shared.Models;

namespace Nakshatra.Shared.Storage;

/// <summary>
/// In-memory repository used when MongoDB is not configured. Keeps the services runnable
/// (and testable) without external infrastructure.
/// </summary>
public class InMemoryDocumentRepository<T> : IDocumentRepository<T> where T : class, IEntity
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, T>> Collections = new();

    private readonly ConcurrentDictionary<string, T> _items;

    public InMemoryDocumentRepository(string collectionName)
    {
        _items = Collections.GetOrAdd(collectionName, _ => new ConcurrentDictionary<string, T>());
    }

    public Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<T>>(_items.Values.ToList());

    public Task<T?> GetAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_items.TryGetValue(id, out var item) ? item : null);

    public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<T>>(_items.Values.Where(predicate.Compile()).ToList());

    public Task<T> UpsertAsync(T entity, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entity.Id))
        {
            entity.Id = Guid.NewGuid().ToString("N");
        }

        _items[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_items.TryRemove(id, out _));
}
