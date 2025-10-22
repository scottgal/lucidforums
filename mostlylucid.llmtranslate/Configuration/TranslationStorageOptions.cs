namespace mostlylucid.llmtranslate.Configuration;

/// <summary>
/// Configuration options for translation storage
/// </summary>
public class TranslationStorageOptions
{
    /// <summary>
    /// Storage provider type
    /// </summary>
    public TranslationStorageType StorageType { get; set; } = TranslationStorageType.PostgreSql;

    /// <summary>
    /// Connection string for database providers (PostgreSQL, SQLite)
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// PostgreSQL schema name (default: "public")
    /// Only used when StorageType is PostgreSql
    /// </summary>
    public string PostgreSqlSchema { get; set; } = "public";

    /// <summary>
    /// File path for JSON file storage
    /// Only used when StorageType is JsonFile
    /// </summary>
    public string? JsonFilePath { get; set; }

    /// <summary>
    /// Whether to auto-save changes to JSON file immediately
    /// Only used when StorageType is JsonFile
    /// Default: true
    /// </summary>
    public bool JsonAutoSave { get; set; } = true;

    /// <summary>
    /// Whether to enable in-memory caching
    /// Default: true
    /// </summary>
    public bool EnableMemoryCache { get; set; } = true;

    /// <summary>
    /// Memory cache duration in minutes
    /// Default: 60 minutes
    /// </summary>
    public int MemoryCacheDurationMinutes { get; set; } = 60;
}

/// <summary>
/// Translation storage provider types
/// </summary>
public enum TranslationStorageType
{
    /// <summary>
    /// PostgreSQL database with optional schema name
    /// </summary>
    PostgreSql,

    /// <summary>
    /// SQLite database (file-based or in-memory)
    /// </summary>
    Sqlite,

    /// <summary>
    /// JSON file storage (for simple deployments or development)
    /// </summary>
    JsonFile
}
