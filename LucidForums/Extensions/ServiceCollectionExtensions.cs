using LucidForums.Data;
using LucidForums.Helpers;
using LucidForums.Models.Configuration;
using LucidForums.Models.Entities;
using LucidForums.Services.Ai;
using LucidForums.Services.Ai.Providers;
using LucidForums.Services.Auth;
using LucidForums.Services.Charters;
using LucidForums.Services.Forum;
using LucidForums.Services.Llm;
using LucidForums.Services.Moderation;
using LucidForums.Services.Observability;
using LucidForums.Services.Search;
using LucidForums.Services.Seeding;
using LucidForums.Services.Admin;
using LucidForums.Services.Analysis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Text;

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
        // JWT config
        services.ConfigurePOCO<JwtOptions>(configuration);
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

    public static IServiceCollection AddLucidForumsAuthentication(this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection("Jwt");
        var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.SaveToken = true;
                options.RequireHttpsMetadata = false; // Set to true in production
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings["Issuer"],
                    ValidAudience = jwtSettings["Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                    ClockSkew = TimeSpan.Zero
                };

                // Support for SignalR (query string token)
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        // Add authorization with policies
        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireUser", policy => policy.RequireRole("User", "Moderator", "Admin"));
            options.AddPolicy("RequireModerator", policy => policy.RequireRole("Moderator", "Admin"));
            options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
        });

        // Register token service
        services.AddScoped<ITokenService, TokenService>();

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
            client.Timeout = TimeSpan.FromSeconds(360);
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
        // EmbeddingService is safe as a singleton (no captured DbContext); it creates scopes per call.
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
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
        services.AddScoped<ICharterScoringService, CharterScoringService>();
        // Search service
        services.AddScoped<ISearchService, SearchService>();
        // Translation services
        services.AddScoped<Services.Translation.RequestTranslationCache>(); // Request-scoped cache to avoid concurrent DbContext access
        services.AddScoped<Services.Translation.ITranslationService, Services.Translation.TranslationService>();
        services.AddScoped<Services.Translation.IContentTranslationService, Services.Translation.ContentTranslationService>();
        services.AddScoped<Services.Translation.IPageLanguageSwitchService, Services.Translation.PageLanguageSwitchService>();
        services.AddScoped<TranslationHelper>();

        // Content translation queue and background service
        services.AddSingleton<Services.Translation.ContentTranslationQueue>();
        services.AddSingleton<Services.Translation.IContentTranslationQueue>(sp => sp.GetRequiredService<Services.Translation.ContentTranslationQueue>());
        services.AddHostedService<Services.Translation.ContentTranslationHostedService>();

        // Setup services for first-run configuration and site generation
        services.AddScoped<Services.Setup.ISetupService, Services.Setup.SetupService>();
        services.AddScoped<Services.Setup.ISiteSetupService, Services.Setup.SiteSetupService>();

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
                .AddSource(TelemetryConstants.ActivitySourceName)) // OTLP exporter disabled temporarily
            .WithMetrics(mp => mp
                .SetResourceBuilder(resourceBuilder)
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation() .AddMeter(TelemetryConstants.MeterName));
                //
                // .AddOtlpExporter(otlp =>
                // {
                //     var endpoint = configuration["Otlp:Endpoint"];
                //     var protocol = configuration["Otlp:Protocol"]; // "grpc" or "http/protobuf"
                //     if (!string.IsNullOrWhiteSpace(endpoint))
                //     {
                //         var uri = new Uri(endpoint);
                //         otlp.Endpoint = uri;
                //         if (string.IsNullOrWhiteSpace(protocol))
                //         {
                //             if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                //                 uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                //             {
                //                 otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                //             }
                //         }
                //     }
                //     if (!string.IsNullOrWhiteSpace(protocol))
                //     {
                //         otlp.Protocol = protocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
                //             ? OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf
                //             : OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                //     }
                // }));

        return services;
    }

    public static IServiceCollection AddLucidForumsAll(this IServiceCollection services, IConfiguration configuration)
    {
        return services
            .AddLucidForumsMvcAndRealtime()
            .AddLucidForumsConfiguration(configuration)
            .AddLucidForumsDatabase(configuration)
            .AddLucidForumsAuthentication(configuration)
            .AddLucidForumsAi()
            .AddLucidForumsSeeding()
            .AddLucidForumsModeration()
            .AddLucidForumsEmbedding()
            .AddLucidForumsDomainServices()
            .AddLucidForumsObservability(configuration);
    }
}