using MongoDB.Driver;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.User.Service;

// The aggregate is authoritative once created, including when its address list is empty.
public sealed class AddressBookDocument : IEntity
{
    public string Id { get; set; } = string.Empty;
    public long Revision { get; set; }
    public List<Address> Addresses { get; set; } = new();
}

public sealed class AddressBookStore(
    IDocumentRepository<Address> legacy,
    IDocumentRepository<AddressBookDocument> books,
    IServiceProvider services)
{
    private static readonly SemaphoreSlim[] MemoryLocks = Enumerable.Range(0, 256)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly IMongoCollection<AddressBookDocument>? collection =
        services.GetService<IMongoDatabase>()?.GetCollection<AddressBookDocument>("addressbookdocuments")
            .WithReadPreference(ReadPreference.Primary)
            .WithReadConcern(ReadConcern.Majority)
            .WithWriteConcern(WriteConcern.WMajority);

    public async Task<List<Address>> ListAsync(string userId, CancellationToken ct) =>
        AddressBook.Ordered((await LoadAsync(userId, ct)).Addresses.Select(Clone));

    public Task<Address?> CreateAsync(string userId, AddressRequest request, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;
        return MutateAsync(userId, addresses =>
        {
            var address = new Address { Id = id, UserId = userId, CreatedAt = createdAt };
            AddressBook.Apply(address, request);
            addresses.Add(address);
            if (addresses.Count == 1 || request.IsDefault == true)
                AddressBook.MakeDefault(addresses, address);
            return address;
        }, ct);
    }

    public Task<Address?> EditAsync(string userId, string addressId, AddressRequest request, CancellationToken ct) =>
        MutateAsync(userId, addresses =>
        {
            var address = addresses.Find(a => a.Id == addressId);
            if (address is null) return null;
            AddressBook.Apply(address, request);
            if (request.IsDefault == true) AddressBook.MakeDefault(addresses, address);
            return address;
        }, ct);

    public Task<Address?> SetDefaultAsync(string userId, string addressId, CancellationToken ct) =>
        MutateAsync(userId, addresses =>
        {
            var address = addresses.Find(a => a.Id == addressId);
            if (address is not null) AddressBook.MakeDefault(addresses, address);
            return address;
        }, ct);

    public async Task<bool> DeleteAsync(string userId, string addressId, CancellationToken ct) =>
        await MutateAsync(userId, addresses =>
        {
            var address = addresses.Find(a => a.Id == addressId);
            if (address is null) return null;
            addresses.Remove(address);
            AddressBook.Normalize(addresses);
            return address;
        }, ct) is not null;

    private async Task<AddressBookDocument> LoadAsync(string userId, CancellationToken ct)
    {
        var existing = collection is null
            ? await books.GetAsync(userId, ct)
            : await collection.Find(b => b.Id == userId).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;

        var initial = new AddressBookDocument
        {
            Id = userId,
            Addresses = (await legacy.FindAsync(a => a.UserId == userId, ct)).Select(Clone).ToList()
        };
        AddressBook.Normalize(initial.Addresses);
        if (collection is null)
        {
            if (await books.TryCreateAsync(initial, ct)) return initial;
            return (await books.GetAsync(userId, ct))!;
        }

        while (true)
        {
            try
            {
                await collection.InsertOneAsync(initial, cancellationToken: ct);
                return initial;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // A concurrent initializer may not yet be majority committed, or may roll back.
                existing = await collection.Find(b => b.Id == userId).FirstOrDefaultAsync(ct);
                if (existing is not null) return existing;
                await Task.Delay(10, ct);
            }
        }
    }

    private async Task<Address?> MutateAsync(
        string userId, Func<List<Address>, Address?> mutation, CancellationToken ct)
    {
        // The fallback repository is shared in-process and stores references, so protect and copy them.
        var gate = collection is null
            ? MemoryLocks[(int)((uint)StringComparer.Ordinal.GetHashCode(userId) % (uint)MemoryLocks.Length)]
            : null;
        if (gate is not null) await gate.WaitAsync(ct);
        try
        {
            while (true)
            {
                var current = await LoadAsync(userId, ct);
                var updated = new AddressBookDocument
                {
                    Id = userId,
                    Revision = checked(current.Revision + 1),
                    Addresses = current.Addresses.Select(Clone).ToList()
                };
                var result = mutation(updated.Addresses);
                if (result is null) return null;

                if (collection is null)
                {
                    await books.UpsertAsync(updated, ct);
                    return Clone(result);
                }

                var filter = Builders<AddressBookDocument>.Filter.Eq(b => b.Id, userId) &
                             Builders<AddressBookDocument>.Filter.Eq(b => b.Revision, current.Revision);
                var write = await collection.ReplaceOneAsync(filter, updated, cancellationToken: ct);
                if (write.MatchedCount == 1) return Clone(result);
            }
        }
        finally
        {
            gate?.Release();
        }
    }

    private static Address Clone(Address a) => new()
    {
        Id = a.Id, UserId = a.UserId, FullName = a.FullName, Line1 = a.Line1, Line2 = a.Line2,
        City = a.City, State = a.State, PostalCode = a.PostalCode, Country = a.Country,
        Phone = a.Phone, IsDefault = a.IsDefault, CreatedAt = a.CreatedAt
    };
}
