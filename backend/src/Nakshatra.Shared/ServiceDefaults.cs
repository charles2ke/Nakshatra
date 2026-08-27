using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Nakshatra.Shared.Caching;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;
using StackExchange.Redis;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nakshatra.Shared;

public static class ServiceDefaults
{
    public const string CorsPolicy = "nakshatra-portal";

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Registers MongoDB, Redis and Kafka infrastructure. Any component without configuration
    /// falls back to an in-process implementation so the service still starts and serves traffic.
    /// </summary>
    public static IServiceCollection AddNakshatraInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
            policy.WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                    ?? new[] { "http://localhost:5173", "http://localhost:4173" })
                .AllowAnyHeader()
                .AllowAnyMethod()));

        services.AddHttpClient();
        services.AddResponseCompression();
        services.AddHealthChecks();

        // Every instance is stateless, so instances can be added or removed freely; the rate limiter
        // therefore protects a single replica and is sized per replica.
        var permitLimit = configuration.GetValue<int?>("RateLimit:PermitsPerMinute") ?? 600;
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        AddMongo(services, configuration);
        AddRedis(services, configuration);
        AddKafka(services, configuration);

        return services;
    }

    private static void AddMongo(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["Mongo:ConnectionString"];
        var databaseName = configuration["Mongo:Database"] ?? "nakshatra";

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton(typeof(IDocumentRepository<>), typeof(InMemoryRepositoryFactoryAdapter<>));
            return;
        }

        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
        services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
        services.AddSingleton(typeof(IDocumentRepository<>), typeof(MongoRepositoryFactoryAdapter<>));
    }

    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<ICacheService, InMemoryCacheService>();
            return;
        }

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<ICacheService, RedisCacheService>();
    }

    private static void AddKafka(IServiceCollection services, IConfiguration configuration)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            services.AddSingleton<InMemoryEventBus>();
            services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InMemoryEventBus>());
            services.AddSingleton<IEventSubscriber>(sp => sp.GetRequiredService<InMemoryEventBus>());
            return;
        }

        var options = new KafkaOptions { BootstrapServers = bootstrapServers };
        services.AddSingleton(options);
        services.AddSingleton<IEventPublisher>(sp => new KafkaEventPublisher(
            options, sp.GetRequiredService<ILogger<KafkaEventPublisher>>()));
        services.AddSingleton<IEventSubscriber>(sp => new KafkaEventSubscriber(
            options, sp.GetRequiredService<ILogger<KafkaEventSubscriber>>()));
    }

    /// <summary>
    /// Applies the shared middleware pipeline: security headers, response compression, rate
    /// limiting, CORS, the health endpoint and the internal event intake endpoint.
    /// </summary>
    public static WebApplication UseNakshatraDefaults(this WebApplication app, string serviceName)
    {
        app.UseResponseCompression();

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            headers["Cache-Control"] = "no-store";
            await next();
        });

        app.UseRateLimiter();
        app.UseCors(CorsPolicy);
        app.MapHealthChecks("/health/ready");
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = serviceName }));

        WarnAboutSingleInstanceFallbacks(app, serviceName);

        // Only present when Kafka is not configured: lets the publishing service hand events to
        // this service's subscribers over HTTP. Protected by a shared token when one is configured.
        var localBus = app.Services.GetService<InMemoryEventBus>();
        if (localBus is not null)
        {
            var internalToken = app.Configuration["Internal:Token"];
            app.MapPost("/internal/events", async (HttpContext context, EventEnvelope envelope, CancellationToken ct) =>
            {
                if (!string.IsNullOrWhiteSpace(internalToken)
                    && !string.Equals(context.Request.Headers["X-Internal-Token"], internalToken, StringComparison.Ordinal))
                {
                    return Results.Unauthorized();
                }

                await localBus.DispatchAsync(envelope, ct);
                return Results.Accepted();
            });
        }

        return app;
    }

    /// <summary>
    /// The in-process cache/store/bus fallbacks are per-instance, so they are only safe for a single
    /// replica. Log loudly when a service starts without the shared infrastructure configured.
    /// </summary>
    private static void WarnAboutSingleInstanceFallbacks(WebApplication app, string serviceName)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Nakshatra.Startup");
        var fallbacks = new List<string>();

        if (string.IsNullOrWhiteSpace(app.Configuration["Mongo:ConnectionString"]))
        {
            fallbacks.Add("MongoDB");
        }

        if (string.IsNullOrWhiteSpace(app.Configuration["Redis:ConnectionString"]))
        {
            fallbacks.Add("Redis");
        }

        if (string.IsNullOrWhiteSpace(app.Configuration["Kafka:BootstrapServers"]))
        {
            fallbacks.Add("Kafka");
        }

        if (fallbacks.Count > 0)
        {
            logger.LogWarning(
                "{Service} started with in-process fallbacks for {Fallbacks}. State is not shared between instances, so run a single replica until the shared infrastructure is configured.",
                serviceName, string.Join(", ", fallbacks));
        }
    }

    /// <summary>Collection naming convention: pluralised lower-case entity name.</summary>
    public static string CollectionNameFor(Type entityType) => entityType.Name.ToLowerInvariant() switch
    {
        "category" => "categories",
        var name => name + "s"
    };
}

internal sealed class InMemoryRepositoryFactoryAdapter<T> : InMemoryDocumentRepository<T> where T : class, IEntity
{
    public InMemoryRepositoryFactoryAdapter() : base(ServiceDefaults.CollectionNameFor(typeof(T)))
    {
    }
}

internal sealed class MongoRepositoryFactoryAdapter<T> : MongoDocumentRepository<T> where T : class, IEntity
{
    public MongoRepositoryFactoryAdapter(IMongoDatabase database)
        : base(database, ServiceDefaults.CollectionNameFor(typeof(T)))
    {
    }
}
