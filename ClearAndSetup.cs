using Npgsql;

var connectionString = "Host=localhost;Port=5432;Database=lucidforums;Username=postgres;Password=postgres";

await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync();

Console.WriteLine("Creating TranslationQueue table...");

await using var createTableCmd = conn.CreateCommand();
createTableCmd.CommandText = @"CREATE TABLE IF NOT EXISTS ""TranslationQueue"" (
    ""Id"" uuid PRIMARY KEY,
    ""ContentType"" text NOT NULL,
    ""ContentId"" text NOT NOT,
    ""FieldName"" text NOT NULL,
    ""Content"" text NOT NULL,
    ""SourceLanguage"" text NOT NULL DEFAULT 'en',
    ""Priority"" integer NOT NULL DEFAULT 0,
    ""AttemptCount"" integer NOT NULL DEFAULT 0,
    ""MaxAttempts"" integer NOT NULL DEFAULT 3,
    ""LastError"" text,
    ""CreatedAtUtc"" timestamp without time zone NOT NULL,
    ""ProcessedAtUtc"" timestamp without time zone,
    ""IsProcessed"" boolean NOT NULL DEFAULT false
)";
await createTableCmd.ExecuteNonQueryAsync();

Console.WriteLine("Creating indexes...");

await using var createIndexCmd = conn.CreateCommand();
createIndexCmd.CommandText = @"
    CREATE INDEX IF NOT EXISTS ""IX_TranslationQueue_IsProcessed_Priority_CreatedAtUtc""
    ON ""TranslationQueue"" (""IsProcessed"", ""Priority"", ""CreatedAtUtc"");

    CREATE INDEX IF NOT EXISTS ""IX_TranslationQueue_ContentType_ContentId_FieldName_IsProcessed""
    ON ""TranslationQueue"" (""ContentType"", ""ContentId"", ""FieldName"", ""IsProcessed"");
";
await createIndexCmd.ExecuteNonQueryAsync();

Console.WriteLine("Clearing all content...");

await using var clearCmd = conn.CreateCommand();
clearCmd.CommandText = @"
    DELETE FROM ""ContentTranslations"";
    DELETE FROM ""TranslationQueue"";
    DELETE FROM ""Messages"";
    DELETE FROM ""Threads"";
    DELETE FROM ""ForumUsers"";
    DELETE FROM ""Forums"";
    DELETE FROM message_embeddings;
";
await clearCmd.ExecuteNonQueryAsync();

Console.WriteLine("✓ All content has been cleared and TranslationQueue table created successfully.");
