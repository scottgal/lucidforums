using LucidForums.Data;
using LucidForums.Helpers;
using LucidForums.Models.Entities;
using LucidForums.Services.Ai;
using LucidForums.Services.Ai.Providers;
using LucidForums.Services.Charters;
using LucidForums.Services.Forum;
using LucidForums.Services.Llm;
using LucidForums.Services.Moderation;
using LucidForums.Services.Observability;
using LucidForums.Services.Search;
using LucidForums.Services.Seeding;
using LucidForums.Services.Admin;
using LucidForums.Services.Analysis;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LucidForums.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLucidForumsConfiguration(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind config using project helpers (choose reloadable vs static)
        services.ConfigureScopedPOCOFromMonitor<AiOptions>(configuration); // reloadable
        services.ConfigurePOCO<OllamaOptions>(configuration); // static singleton
        services.ConfigureScopedPOCOFromMonitor<EmbeddingOptions>(configuration); // reloadable
        services.Configure<EmbeddingOptions>(configuration.GetSection("Embedding"));
        // Telemetry config (names, tags, paths)
        services.ConfigurePOCO<TelemetryOptions>(configuration);
        return services;
    }

    public static IServiceCollection AddLucidForumsDatabase(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register the primary DbContext for scoped usage (web requests)
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var cs = configuration.GetConnectionString("Default")
                     ?? configuration["ConnectionStrings:Default"]
                     ?? configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(cs))
            {
                throw new InvalidOperationException("Database connection string not found. Please set 'ConnectionStrings:Default' in appsettings or user-secrets.");
            }

            var csNorm = cs.Trim();
            var upper = csNorm.ToUpperInvariant();

            // Guard: reject SQLite-style connection strings (ltree and vector require PostgreSQL)
            bool looksLikeSqlite = upper.Contains("DATA SOURCE=") || csNorm.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || csNorm.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase);
            if (looksLikeSqlite)
            {
                throw new InvalidOperationException("SQLite is not supported. Please provide a PostgreSQL connection string (e.g., Host=localhost;Port=5432;Database=...;Username=...;Password=...).");
            }

            // Always use PostgreSQL (Npgsql)
            options.UseNpgsql(cs);
        }, contextLifetime: ServiceLifetime.Scoped, optionsLifetime: ServiceLifetime.Singleton);

        // Also register a DbContext factory for singletons/background services
        services.AddDbContextFactory<ApplicationDbContext>();

        services.AddIdentity<User, IdentityRole>(options => { options.SignIn.RequireConfirmedAccount = false; })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }

    public static IServiceCollection AddLucidForumsAi(this IServiceCollection services)
    {
        // Endpoint provider + HttpClient
        services.AddSingleton<IOllamaEndpointProvider, OllamaEndpointProvider>();
        services.AddHttpClient("ollama", (sp, client) =>
        {
            var ep = sp.GetRequiredService<IOllamaEndpointProvider>();
            client.BaseAddress = ep.GetBaseAddress();
        });

        // Pluggable chat providers
        services.AddSingleton<IChatProvider, OllamaChatProvider>();
        services.AddSingleton<IChatProvider, LmStudioChatProvider>();

        // Core AI services
        services.AddSingleton<IAiSettingsService, AiSettingsService>();
        services.AddSingleton<ITextAiService, TextAiService>();
        services.AddSingleton<IImageAiService, ImageAiService>();
        services.AddSingleton<IOllamaChatService, OllamaChatAdapter>();

        return services;
    }

    public static IServiceCollection AddLucidForumsSeeding(this IServiceCollection services)
    {
        services.AddSingleton<IForumSeedingQueue, ForumSeedingQueue>();
        services.AddSingleton<ISeedingProgressStore, InMemorySeedingProgressStore>();
        services.AddHostedService<ForumSeedingHostedService>();
        return services;
    }

    public static IServiceCollection AddLucidForumsModeration(this IServiceCollection services)
    {
        services.AddSingleton<IModerationService, ModerationService>();
        return services;
    }

    public static IServiceCollection AddLucidForumsEmbedding(this IServiceCollection services)
    {
        services.AddScoped<IEmbeddingService, EmbeddingService>();
        return services;
    }

    public static IServiceCollection AddLucidForumsDomainServices(this IServiceCollection services)
    {
        services.AddScoped<IForumService, ForumService>();
        services.AddScoped<IThreadService, ThreadService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IThreadViewService, ThreadViewService>();
        services.AddScoped<ICharterService, CharterService>();
        services.AddScoped<IAdminMaintenanceService, AdminMaintenanceService>();
        // Analysis helpers (tags and tone advice)
        services.AddScoped<ITagExtractionService, TagExtractionService>();
        services.AddScoped<IToneAdvisor, ToneAdvisor>();
        return services;
    }

    public static IServiceCollection AddLucidForumsMvcAndRealtime(this IServiceCollection services)
    {
        services.AddControllersWithViews();
        services.AddRazorPages();
        services.AddRazorComponents();
        services.AddSignalR();
        return services;
    }

    public static IServiceCollection AddLucidForumsObservability(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Resource describing this service
        var serviceName = configuration["Service:Name"] ?? "LucidForums";
        var serviceVersion = configuration["Service:Version"] ?? "1.0.0";
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: serviceVersion)
            .AddTelemetrySdk();

        // App-level ActivitySource/Meter for custom spans/metrics
        services.AddSingleton<ITelemetry, Telemetry>();

        services.AddOpenTelemetry()
            .ConfigureResource(rb => rb.AddService(serviceName, serviceVersion: serviceVersion))
            .WithTracing(tp => tp
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource(TelemetryConstants.ActivitySourceName)
                .AddOtlpExporter(otlp =>
                {
                    var endpoint = configuration["Otlp:Endpoint"];
                    if (!string.IsNullOrWhiteSpace(endpoint)) otlp.Endpoint = new Uri(endpoint);
                }))
            .WithMetrics(mp => mp
                .SetResourceBuilder(resourceBuilder)
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(TelemetryConstants.MeterName)
                .AddOtlpExporter(otlp =>
                {
                    var endpoint = configuration["Otlp:Endpoint"];
                    if (!string.IsNullOrWhiteSpace(endpoint)) otlp.Endpoint = new Uri(endpoint);
                }));

        return services;
    }

    public static IServiceCollection AddLucidForumsAll(this IServiceCollection services, IConfiguration configuration)
    {
        return services
            .AddLucidForumsMvcAndRealtime()
            .AddLucidForumsConfiguration(configuration)
            .AddLucidForumsDatabase(configuration)
            .AddLucidForumsAi()
            .AddLucidForumsSeeding()
            .AddLucidForumsModeration()
            .AddLucidForumsEmbedding()
            .AddLucidForumsDomainServices()
            .AddLucidForumsObservability(configuration);
    }
}