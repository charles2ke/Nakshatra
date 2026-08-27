using System.Linq.Expressions;
using MongoDB.Driver;
using Nakshatra.Shared.Models;

namespace Nakshatra.Shared.Storage;

/// <summary>MongoDB backed document repository.</summary>
public class MongoDocumentRepository<T> : IDocumentRepository<T> where T : class, IEntity
{
    private readonly IMongoCollection<T> _collection;

    public MongoDocumentRepository(IMongoDatabase database, string collectionName)
    {
        _collection = database.GetCollection<T>(collectionName);
    }

    public async Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default)
        => await _collection.Find(Builders<T>.Filter.Empty).ToListAsync(ct);

    public async Task<T?> GetAsync(string id, CancellationToken ct = default)
        => await _collection.Find(x => x.Id == id).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await _collection.Find(predicate).ToListAsync(ct);

    public async Task<T> UpsertAsync(T entity, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entity.Id))
        {
            entity.Id = Guid.NewGuid().ToString("N");
        }

        await _collection.ReplaceOneAsync(
            x => x.Id == entity.Id,
            entity,
            new ReplaceOptions { IsUpsert = true },
            ct);
        return entity;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var result = await _collection.DeleteOneAsync(x => x.Id == id, ct);
        return result.DeletedCount > 0;
    }
}
