using System.Linq.Expressions;

namespace Nakshatra.Shared.Storage;

using Nakshatra.Shared.Models;

/// <summary>
/// Document repository abstraction. Backed by MongoDB in real deployments and by an
/// in-memory store when no MongoDB connection string is configured (local dev / tests).
/// </summary>
public interface IDocumentRepository<T> where T : class, IEntity
{
    Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default);
    Task<T?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<T> UpsertAsync(T entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
