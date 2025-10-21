using LucidForums.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace LucidForums.Extensions;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseLucidForumsPipeline(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        // Request logging via Serilog
        app.UseSerilogRequestLogging();

        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapStaticAssets();
        return app;
    }
    public static WebApplication MapLucidForumsEndpoints(this WebApplication app)
    {
        // Attribute-routed API controllers
        app.MapControllers();

        // Default MVC route
        app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
            .WithStaticAssets();

        // Razor Pages
        app.MapRazorPages();

        // SignalR hubs
        app.MapHub<LucidForums.Hubs.ForumHub>(LucidForums.Hubs.ForumHub.HubPath);
        app.MapHub<LucidForums.Hubs.SeedingHub>(LucidForums.Hubs.SeedingHub.HubPath);

        return app;
    }

    public static WebApplication InitializeLucidForumsDatabase(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();
        try
        {
            // Enable required extensions (handled inside SQL to avoid noisy errors when lacking privileges)
            db.Database.ExecuteSqlRaw(@"DO $$ BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector') THEN
                CREATE EXTENSION vector;
            END IF;
        EXCEPTION
            WHEN insufficient_privilege THEN
                NULL;
            WHEN OTHERS THEN
                NULL;
        END $$;");

            // Ensure Threads.RootMessageId is nullable (for existing PostgreSQL schemas created before the fix)
            // Safe to run multiple times; no-op if already nullable or table missing.
            db.Database.ExecuteSqlRaw(@"DO $$ BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns 
                WHERE table_schema = 'public' AND table_name = 'Threads' AND column_name = 'RootMessageId'
            ) THEN
                EXECUTE 'ALTER TABLE ""Threads"" ALTER COLUMN ""RootMessageId"" DROP NOT NULL';
            END IF;
        EXCEPTION WHEN OTHERS THEN
            -- Ignore any errors (e.g., insufficient privileges, SQLite provider, etc.)
            NULL;
        END $$;");

            // Create embeddings table
            db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS message_embeddings (
            message_id uuid PRIMARY KEY,
            forum_id uuid NOT NULL,
            thread_id uuid NOT NULL,
            content_hash text NOT NULL,
            embedding vector,
            created_at timestamptz NOT NULL DEFAULT now()
        )");

            // Create IVFFlat index if not exists (wrapped in DO block to avoid errors if operator class missing)
            db.Database.ExecuteSqlRaw(@"DO $$ BEGIN
            CREATE INDEX IF NOT EXISTS idx_message_embeddings_ivfflat ON message_embeddings USING ivfflat (embedding vector_cosine_ops);
        EXCEPTION WHEN OTHERS THEN
            -- ignore if cannot create (e.g., extension not supporting ivfflat); sequential scan will be used
            NULL;
        END $$;");

            // Ensure AppSettings table exists (case-sensitive EF Core default) and has a single row with Id=1
            db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS ""AppSettings"" (
                ""Id"" integer PRIMARY KEY,
                ""GenerationProvider"" text NULL,
                ""GenerationModel"" text NULL,
                ""TranslationProvider"" text NULL,
                ""TranslationModel"" text NULL,
                ""EmbeddingProvider"" text NULL,
                ""EmbeddingModel"" text NULL
            )");
            // Insert default row if absent
            db.Database.ExecuteSqlRaw(@"INSERT INTO ""AppSettings"" (""Id"", ""GenerationProvider"", ""GenerationModel"", ""TranslationProvider"", ""TranslationModel"", ""EmbeddingProvider"", ""EmbeddingModel"")
            VALUES (1, NULL, NULL, NULL, NULL, NULL, NULL)
            ON CONFLICT (""Id"") DO NOTHING;");
        }
        catch
        {
            // Ignore when not PostgreSQL or extension unavailable
        }

        return app;
    }
}
