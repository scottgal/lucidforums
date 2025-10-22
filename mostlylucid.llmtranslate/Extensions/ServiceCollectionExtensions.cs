using mostlylucid.llmtranslate.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using mostlylucid.llmtranslate.Configuration;
using mostlylucid.llmtranslate.Data;
using mostlylucid.llmtranslate.Helpers;
using mostlylucid.llmtranslate.Services;
using mostlylucid.llmtranslate.Storage;

namespace mostlylucid.llmtranslate.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds auto-translate services using configuration options
    /// This is the recommended method for most scenarios
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Action to configure storage options</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAutoTranslate(
        this IServiceCollection services,
        Action<TranslationStorageOptions> configureOptions)
    {
        var options = new TranslationStorageOptions();
        configureOptions(options);

        return services.AddAutoTranslate(options);
    }

    /// <summary>
    /// Adds auto-translate services using configuration options instance
    /// </summary>
    public static IServiceCollection AddAutoTranslate(
        this IServiceCollection services,
        TranslationStorageOptions options)
    {
        // Register common services
        services.AddScoped<RequestTranslationCache>();
        services.AddScoped<TranslationHelper>();
        services.AddScoped<IPageLanguageSwitchService, PageLanguageSwitchService>();

        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddSignalR();
        services.AddControllers();

        // Configure storage based on type
        switch (options.StorageType)
        {
            case TranslationStorageType.PostgreSql:
                if (string.IsNullOrEmpty(options.ConnectionString))
                    throw new ArgumentException("ConnectionString is required for PostgreSQL storage");

                services.AddDbContext<TranslationDbContext>(dbOptions =>
                {
                    dbOptions.UseNpgsql(options.ConnectionString);
                }, ServiceLifetime.Scoped);

                services.AddScoped<ITranslationDbContext>(sp =>
                {
                    var dbContextOptions = sp.GetRequiredService<DbContextOptions<TranslationDbContext>>();
                    return new TranslationDbContext(dbContextOptions, options.PostgreSqlSchema);
                });

                services.AddScoped<ITranslationService, TranslationService>();
                break;

            case TranslationStorageType.Sqlite:
                if (string.IsNullOrEmpty(options.ConnectionString))
                    throw new ArgumentException("ConnectionString is required for SQLite storage");

                services.AddDbContext<TranslationDbContext>(dbOptions =>
                {
                    dbOptions.UseSqlite(options.ConnectionString);
                }, ServiceLifetime.Scoped);

                services.AddScoped<ITranslationDbContext>(sp =>
                    sp.GetRequiredService<TranslationDbContext>());

                services.AddScoped<ITranslationService, TranslationService>();
                break;

            case TranslationStorageType.JsonFile:
                if (string.IsNullOrEmpty(options.JsonFilePath))
                    throw new ArgumentException("JsonFilePath is required for JSON file storage");

                services.AddSingleton(sp =>
                    new JsonFileTranslationStore(
                        options.JsonFilePath,
                        options.JsonAutoSave,
                        sp.GetRequiredService<ILogger<JsonFileTranslationStore>>()));

                services.AddScoped<ITranslationService>(sp =>
                    new JsonFileTranslationService(
                        sp.GetRequiredService<JsonFileTranslationStore>(),
                        sp.GetRequiredService<IAiTranslationProvider>(),
                        sp.GetRequiredService<IMemoryCache>(),
                        sp.GetRequiredService<RequestTranslationCache>(),
                        sp.GetRequiredService<ILogger<JsonFileTranslationService>>(),
                        options.EnableMemoryCache,
                        options.MemoryCacheDurationMinutes));
                break;

            default:
                throw new ArgumentException($"Unsupported storage type: {options.StorageType}");
        }

        return services;
    }

    /// <summary>
    /// Adds auto-translate services with PostgreSQL database
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="connectionString">PostgreSQL connection string</param>
    /// <param name="schema">PostgreSQL schema name (default: "public")</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAutoTranslateWithPostgreSql(
        this IServiceCollection services,
        string connectionString,
        string schema = "public")
    {
        return services.AddAutoTranslate(options =>
        {
            options.StorageType = TranslationStorageType.PostgreSql;
            options.ConnectionString = connectionString;
            options.PostgreSqlSchema = schema;
        });
    }

    /// <summary>
    /// Adds auto-translate services with SQLite database
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="connectionString">SQLite connection string (e.g., "Data Source=translations.db")</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAutoTranslateWithSqlite(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddAutoTranslate(options =>
        {
            options.StorageType = TranslationStorageType.Sqlite;
            options.ConnectionString = connectionString;
        });
    }

    /// <summary>
    /// Adds auto-translate services with JSON file storage
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="filePath">Path to JSON file</param>
    /// <param name="autoSave">Whether to auto-save changes immediately (default: true)</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAutoTranslateWithJsonFile(
        this IServiceCollection services,
        string filePath,
        bool autoSave = true)
    {
        return services.AddAutoTranslate(options =>
        {
            options.StorageType = TranslationStorageType.JsonFile;
            options.JsonFilePath = filePath;
            options.JsonAutoSave = autoSave;
        });
    }

    /// <summary>
    /// Adds auto-translate services using an existing DbContext that implements ITranslationDbContext
    /// </summary>
    /// <typeparam name="TContext">Your DbContext type that implements ITranslationDbContext</typeparam>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAutoTranslateWithExistingDbContext<TContext>(
        this IServiceCollection services)
        where TContext : DbContext, ITranslationDbContext
    {
        // Register the existing DbContext as ITranslationDbContext
        services.AddScoped<ITranslationDbContext>(sp => sp.GetRequiredService<TContext>());

        // Add auto-translate services (without registering TranslationDbContext)
        services.AddScoped<RequestTranslationCache>();
        services.AddScoped<ITranslationService, TranslationService>();
        services.AddScoped<IPageLanguageSwitchService, PageLanguageSwitchService>();
        services.AddScoped<TranslationHelper>();

        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddSignalR();
        services.AddControllers();

        return services;
    }
}
