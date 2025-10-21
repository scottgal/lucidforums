using LucidForums.Helpers;
using LucidForums.Services.Charters;
using LucidForums.Services.Llm;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace LucidForums.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLucidForumsConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind config using project helpers (choose reloadable vs static)
        services.ConfigureScopedPOCOFromMonitor<LucidForums.Services.Ai.AiOptions>(configuration); // reloadable
        services.ConfigurePOCO<LucidForums.Services.Llm.OllamaOptions>(configuration); // static singleton
        services.ConfigureScopedPOCOFromMonitor<LucidForums.Services.Search.EmbeddingOptions>(configuration); // reloadable
        services.Configure<LucidForums.Services.Search.EmbeddingOptions>(configuration.GetSection("Embedding"));
        // Telemetry config (names, tags, paths)
        services.ConfigurePOCO<LucidForums.Services.Observability.TelemetryOptions>(configuration);
        return services;
    }

    public static IServiceCollection AddLucidForumsDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<LucidForums.Data.ApplicationDbContext>(options =>
        {
            var cs = configuration.GetConnectionString("Default")
                     ?? configuration["ConnectionStrings:Default"]
                     ?? configuration.GetConnectionString("DefaultConnection")
                     ?? "Data Source=app.db";

            if (!string.IsNullOrWhiteSpace(cs) && cs.Contains("Host=", StringComparison.OrdinalIgnoreCase))
            {
                options.UseNpgsql(cs);
            }
            else
            {
                options.UseSqlite(cs);
            }
        });

        services.AddIdentity<LucidForums.Models.Entities.User, IdentityRole>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<LucidForums.Data.ApplicationDbContext>()
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
        services.AddSingleton<LucidForums.Services.Ai.IChatProvider, LucidForums.Services.Ai.Providers.OllamaChatProvider>();
        services.AddSingleton<LucidForums.Services.Ai.IChatProvider, LucidForums.Services.Ai.Providers.LmStudioChatProvider>();

        // Core AI services
        services.AddSingleton<LucidForums.Services.Ai.ITextAiService, LucidForums.Services.Ai.TextAiService>();
        services.AddSingleton<LucidForums.Services.Ai.IImageAiService, LucidForums.Services.Ai.ImageAiService>();
        services.AddSingleton<LucidForums.Services.Llm.IOllamaChatService, LucidForums.Services.Ai.OllamaChatAdapter>();

        return services;
    }

    public static IServiceCollection AddLucidForumsSeeding(this IServiceCollection services)
    {
        services.AddSingleton<LucidForums.Services.Seeding.IForumSeedingQueue, LucidForums.Services.Seeding.ForumSeedingQueue>();
        services.AddHostedService<LucidForums.Services.Seeding.ForumSeedingHostedService>();
        return services;
    }

    public static IServiceCollection AddLucidForumsModeration(this IServiceCollection services)
    {
        services.AddSingleton<LucidForums.Services.Moderation.IModerationService, LucidForums.Services.Moderation.ModerationService>();
        return services;
    }

    public static IServiceCollection AddLucidForumsEmbedding(this IServiceCollection services)
    {
        services.AddScoped<LucidForums.Services.Search.IEmbeddingService, LucidForums.Services.Search.EmbeddingService>();
        return services;
    }

    public static IServiceCollection AddLucidForumsDomainServices(this IServiceCollection services)
    {
        services.AddScoped<LucidForums.Services.Forum.IForumService, LucidForums.Services.Forum.ForumService>();
        services.AddScoped<LucidForums.Services.Forum.IThreadService, LucidForums.Services.Forum.ThreadService>();
        services.AddScoped<LucidForums.Services.Forum.IMessageService, LucidForums.Services.Forum.MessageService>();
        services.AddScoped<LucidForums.Services.Forum.IThreadViewService, LucidForums.Services.Forum.ThreadViewService>();
        services.AddScoped<ICharterService, CharterService>();
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

    public static IServiceCollection AddLucidForumsObservability(this IServiceCollection services, IConfiguration configuration)
    {
        // Resource describing this service
        var serviceName = configuration["Service:Name"] ?? "LucidForums";
        var serviceVersion = configuration["Service:Version"] ?? "1.0.0";
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
            .AddTelemetrySdk();

        // App-level ActivitySource/Meter for custom spans/metrics
        services.AddSingleton<LucidForums.Services.Observability.ITelemetry, LucidForums.Services.Observability.Telemetry>();

        services.AddOpenTelemetry()
            .ConfigureResource(rb => rb.AddService(serviceName: serviceName, serviceVersion: serviceVersion))
            .WithTracing(tp => tp
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource(LucidForums.Services.Observability.TelemetryConstants.ActivitySourceName)
                .AddOtlpExporter(otlp =>
                {
                    var endpoint = configuration["Otlp:Endpoint"];
                    if (!string.IsNullOrWhiteSpace(endpoint))
                    {
                        otlp.Endpoint = new Uri(endpoint);
                    }
                }))
            .WithMetrics(mp => mp
                .SetResourceBuilder(resourceBuilder)
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(LucidForums.Services.Observability.TelemetryConstants.MeterName)
                .AddOtlpExporter(otlp =>
                {
                    var endpoint = configuration["Otlp:Endpoint"];
                    if (!string.IsNullOrWhiteSpace(endpoint))
                    {
                        otlp.Endpoint = new Uri(endpoint);
                    }
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
