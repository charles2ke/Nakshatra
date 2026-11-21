using Nakshatra.Shared;
using Nakshatra.Shared.Endpoints;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;
using Nakshatra.User.Service;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddSingleton<AddressBookStore>();

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

addresses.MapGet("", async (string userId, IDocumentRepository<User> users, AddressBookStore books, CancellationToken ct) =>
{
    if (await users.GetAsync(userId, ct) is null)
    {
        return Results.NotFound(new { error = $"User '{userId}' was not found." });
    }

    return Results.Ok(await books.ListAsync(userId, ct));
});

addresses.MapPost("", async (
    string userId,
    AddressRequest request,
    IDocumentRepository<User> users,
    AddressBookStore books,
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

    var address = (await books.CreateAsync(userId, request, ct))!;
    return Results.Created($"/api/users/{userId}/addresses/{address.Id}", address);
});

addresses.MapPut("/{addressId}", async (
    string userId,
    string addressId,
    AddressRequest request,
    AddressBookStore books,
    CancellationToken ct) =>
{
    if (AddressBook.Validate(request) is { } error)
    {
        return Results.BadRequest(new { error });
    }

    var address = await books.EditAsync(userId, addressId, request, ct);
    if (address is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(address);
});

addresses.MapPost("/{addressId}/default", async (string userId, string addressId, AddressBookStore books, CancellationToken ct) =>
{
    var address = await books.SetDefaultAsync(userId, addressId, ct);
    if (address is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(address);
});

addresses.MapDelete("/{addressId}", async (string userId, string addressId, AddressBookStore books, CancellationToken ct) =>
{
    if (!await books.DeleteAsync(userId, addressId, ct))
    {
        return Results.NotFound();
    }

    return Results.NoContent();
});

await SeedData.SeedAsync(app.Services, SeedData.Users);
app.Run();

public partial class Program;
