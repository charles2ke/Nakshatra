using Nakshatra.Shared;
using Nakshatra.Shared.Endpoints;
using Nakshatra.Shared.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseNakshatraDefaults("vendor-service");

// Vendor master data, read-through cached in Redis.
app.MapCachedCrud<Vendor>("/api/vendors", "vendor", TimeSpan.FromMinutes(15));

await SeedData.SeedAsync(app.Services, SeedData.Vendors);
app.Run();

public partial class Program;
