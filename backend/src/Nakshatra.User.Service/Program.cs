using Nakshatra.Shared;
using Nakshatra.Shared.Endpoints;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;
using Nakshatra.User.Service;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseNakshatraDefaults("user-service");

// Users are cached in Redis; every write invalidates the affected cache keys.
app.MapCachedCrud<User>("/api/users", "user", TimeSpan.FromMinutes(10),
    user => user.CreatedAt = DateTime.UtcNow);

// Personas drive which screens the portal renders for a signed-in user.
app.MapGet("/api/personas", () => Results.Ok(Enum.GetNames<Persona>()));

app.MapGet("/api/users/by-persona/{persona}", async (string persona, IDocumentRepository<User> repo) =>
{
    if (!Enum.TryParse<Persona>(persona, ignoreCase: true, out var parsed))
    {
        return Results.BadRequest(new { error = $"Unknown persona '{persona}'." });
    }

    return Results.Ok(await repo.FindAsync(u => u.Persona == parsed));
});

// Saved shipping addresses used at checkout.
var addresses = app.MapGroup("/api/users/{userId}/addresses");

addresses.MapGet("", async (string userId, IDocumentRepository<User> users, IDocumentRepository<Address> repo, CancellationToken ct) =>
{
    if (await users.GetAsync(userId, ct) is null)
    {
        return Results.NotFound(new { error = $"User '{userId}' was not found." });
    }

    return Results.Ok(AddressBook.Ordered(await repo.FindAsync(a => a.UserId == userId, ct)));
});

addresses.MapPost("", async (
    string userId,
    AddressRequest request,
    IDocumentRepository<User> users,
    IDocumentRepository<Address> repo,
    CancellationToken ct) =>
{
    if (AddressBook.Validate(request) is { } error)
    {
        return Results.BadRequest(new { error });
    }

    if (await users.GetAsync(userId, ct) is null)
    {
        return Results.NotFound(new { error = $"User '{userId}' was not found." });
    }

    var address = new Address { UserId = userId };
    AddressBook.Apply(address, request);
    var isFirst = (await repo.FindAsync(a => a.UserId == userId, ct)).Count == 0;
    if (isFirst || request.IsDefault == true)
    {
        await AddressBook.MakeDefaultAsync(repo, address, ct);
    }
    else
    {
        await repo.UpsertAsync(address, ct);
    }

    return Results.Created($"/api/users/{userId}/addresses/{address.Id}", address);
});

addresses.MapPut("/{addressId}", async (
    string userId,
    string addressId,
    AddressRequest request,
    IDocumentRepository<Address> repo,
    CancellationToken ct) =>
{
    if (AddressBook.Validate(request) is { } error)
    {
        return Results.BadRequest(new { error });
    }

    var address = await repo.GetAsync(addressId, ct);
    if (address is null || address.UserId != userId)
    {
        return Results.NotFound();
    }

    AddressBook.Apply(address, request);
    // The default can be moved to this address but not simply removed, so the book always has one.
    if (request.IsDefault == true)
    {
        await AddressBook.MakeDefaultAsync(repo, address, ct);
    }
    else
    {
        await repo.UpsertAsync(address, ct);
    }

    return Results.Ok(address);
});

addresses.MapPost("/{addressId}/default", async (string userId, string addressId, IDocumentRepository<Address> repo, CancellationToken ct) =>
{
    var address = await repo.GetAsync(addressId, ct);
    if (address is null || address.UserId != userId)
    {
        return Results.NotFound();
    }

    await AddressBook.MakeDefaultAsync(repo, address, ct);
    return Results.Ok(address);
});

addresses.MapDelete("/{addressId}", async (string userId, string addressId, IDocumentRepository<Address> repo, CancellationToken ct) =>
{
    var address = await repo.GetAsync(addressId, ct);
    if (address is null || address.UserId != userId)
    {
        return Results.NotFound();
    }

    await repo.DeleteAsync(addressId, ct);
    if (address.IsDefault)
    {
        await AddressBook.PromoteNewestAsync(repo, userId, ct);
    }

    return Results.NoContent();
});

await SeedData.SeedAsync(app.Services, SeedData.Users);
app.Run();

public partial class Program;
