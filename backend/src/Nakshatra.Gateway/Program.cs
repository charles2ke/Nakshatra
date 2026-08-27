using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Single entry point for the React portal: routes /api/* to the owning microservice.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(options => options.AddPolicy("nakshatra-portal", policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:5173", "http://localhost:4173" })
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

app.UseCors("nakshatra-portal");
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "gateway" }));
app.MapReverseProxy();
app.Run();

public partial class Program;
