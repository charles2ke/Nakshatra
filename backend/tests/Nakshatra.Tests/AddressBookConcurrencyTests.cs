using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;
using Nakshatra.User.Service;

namespace Nakshatra.Tests;

public class AddressBookConcurrencyTests
{
    [Fact]
    public async Task In_memory_operations_are_atomic_and_migrate_legacy_data()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await ExerciseAsync(
            new InMemoryDocumentRepository<Address>("addresses"),
            new InMemoryDocumentRepository<AddressBookDocument>("addressbookdocuments"), services);
    }

    [MongoFact]
    public async Task Mongo_operations_are_atomic_and_migrate_legacy_data_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("NAKSHATRA_TEST_MONGO")!;
        var client = new MongoClient(connectionString);
        var name = $"address_book_test_{Guid.NewGuid():N}";
        var database = client.GetDatabase(name);
        using var services = new ServiceCollection().AddSingleton<IMongoDatabase>(database).BuildServiceProvider();
        try
        {
            await ExerciseAsync(new MongoDocumentRepository<Address>(database, "addresses"),
                new MongoDocumentRepository<AddressBookDocument>(database, "addressbookdocuments"), services);
        }
        finally
        {
            await client.DropDatabaseAsync(name);
        }
    }

    private static async Task ExerciseAsync(IDocumentRepository<Address> legacy,
        IDocumentRepository<AddressBookDocument> documents, IServiceProvider services)
    {
        var user = Guid.NewGuid().ToString("N");
        var old = new Address { UserId = user, FullName = "Old", IsDefault = true, CreatedAt = DateTime.UtcNow.AddDays(-1) };
        var newer = new Address { UserId = user, FullName = "Newer", IsDefault = true, CreatedAt = DateTime.UtcNow };
        await legacy.UpsertAsync(old);
        await legacy.UpsertAsync(newer);
        var first = new AddressBookStore(legacy, documents, services);
        var second = new AddressBookStore(legacy, documents, services);

        var migrated = await first.ListAsync(user, default);
        Assert.Equal(newer.Id, Assert.Single(migrated, a => a.IsDefault).Id);
        var noDefaultUser = Guid.NewGuid().ToString("N");
        var noDefault = new Address { UserId = noDefaultUser, IsDefault = false };
        await legacy.UpsertAsync(noDefault);
        Assert.True(Assert.Single(await second.ListAsync(noDefaultUser, default)).IsDefault);

        // Initialization, first writes, and conflicting default changes race across store instances.
        var newUser = Guid.NewGuid().ToString("N");
        var firstWrites = await Together(Enumerable.Range(0, 20)
            .Select(i => (Func<Task<Address?>>)(() => (i % 2 == 0 ? first : second)
                .CreateAsync(newUser, Request($"First {i}", false), default))));
        Assert.All(firstWrites, a => Assert.NotNull(a));
        var firstBook = await first.ListAsync(newUser, default);
        Assert.Equal(20, firstBook.Count);
        Assert.Single(firstBook, a => a.IsDefault);

        var migratingUser = Guid.NewGuid().ToString("N");
        var migratingAddress = new Address { UserId = migratingUser };
        await legacy.UpsertAsync(migratingAddress);
        await Together(Enumerable.Range(0, 16)
            .Select(i => (Func<Task<Address?>>)(() => (i % 2 == 0 ? first : second)
                .CreateAsync(migratingUser, Request($"Migration {i}", true), default))));
        var migratedRace = await second.ListAsync(migratingUser, default);
        Assert.Equal(17, migratedRace.Count);
        Assert.Contains(migratedRace, a => a.Id == migratingAddress.Id);
        Assert.Single(migratedRace, a => a.IsDefault);

        var creations = await Together(Enumerable.Range(0, 24)
            .Select(i => (Func<Task<Address?>>)(() => (i % 2 == 0 ? first : second)
                .CreateAsync(user, Request($"Created {i}", true), default))));
        Assert.All(creations, a => Assert.NotNull(a));
        var all = await first.ListAsync(user, default);
        Assert.Equal(26, all.Count);
        Assert.Single(all, a => a.IsDefault);
        Assert.Contains(all, a => a.Id == old.Id);
        Assert.Contains(all, a => a.Id == newer.Id);

        var targets = all.Take(12).ToArray();
        var operations = targets.SelectMany((a, i) => new Func<Task<Address?>>[]
        {
            () => first.SetDefaultAsync(user, a.Id, default),
            () => second.EditAsync(user, a.Id, Request($"Edited {i}", i % 2 == 0), default)
        });
        var results = await Together(operations);
        Assert.All(results, a => Assert.NotNull(a));
        all = await second.ListAsync(user, default);
        Assert.Equal(26, all.Count);
        Assert.Single(all, a => a.IsDefault);
        Assert.All(targets, a => Assert.StartsWith("Edited ", all.Single(x => x.Id == a.Id).FullName));

        var readersAndWriters = Enumerable.Range(0, 32).Select(i => (Func<Task<bool>>)(async () =>
        {
            if (i % 2 == 0)
                return await first.SetDefaultAsync(user, targets[i % targets.Length].Id, default) is not null;
            var snapshot = await second.ListAsync(user, default);
            return snapshot.Count == 26 && snapshot.Count(a => a.IsDefault) == 1;
        }));
        Assert.All(await Together(readersAndWriters), Assert.True);

        var victim = all[0];
        var deleteRace = Enumerable.Range(0, 24).Select(i => (Func<Task<bool>>)(async () =>
            i == 0 ? await first.DeleteAsync(user, victim.Id, default) :
            i % 2 == 0 ? await second.SetDefaultAsync(user, victim.Id, default) is not null :
            await second.EditAsync(user, victim.Id, Request("Race", true), default) is not null));
        var deleteResults = await Together(deleteRace);
        Assert.True(deleteResults[0]);
        all = await first.ListAsync(user, default);
        Assert.DoesNotContain(all, a => a.Id == victim.Id);
        Assert.Single(all, a => a.IsDefault);

        await Together(all.Select(a => (Func<Task<bool>>)(() => first.DeleteAsync(user, a.Id, default))));
        Assert.Empty(await second.ListAsync(user, default));
        // An empty migrated aggregate must not reload the old individual documents.
        Assert.Empty(await new AddressBookStore(legacy, documents, services).ListAsync(user, default));
        var replacement = await second.CreateAsync(user, Request("Replacement", false), default);
        Assert.True(replacement!.IsDefault);
        Assert.Single(await first.ListAsync(user, default));
    }

    public sealed class MongoFactAttribute : FactAttribute
    {
        public MongoFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NAKSHATRA_TEST_MONGO")))
                Skip = "Set NAKSHATRA_TEST_MONGO to run MongoDB integration tests.";
        }
    }

    private static AddressRequest Request(string name, bool isDefault) =>
        new(name, "Street", "", "City", "", "123", "Country", "", isDefault);

    private static async Task<T[]> Together<T>(IEnumerable<Func<Task<T>>> actions)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = actions.Select(action => Task.Run(async () =>
        {
            await start.Task;
            return await action();
        })).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }
}
