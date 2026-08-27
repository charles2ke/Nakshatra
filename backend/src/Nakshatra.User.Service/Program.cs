using Nakshatra.Shared;
using Nakshatra.Shared.Endpoints;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

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

await SeedData.SeedAsync(app.Services, SeedData.Users);
app.Run();

public partial class Program;
