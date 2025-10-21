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
            // Enable required extensions (no-op on SQLite)
            db.Database.ExecuteSqlRaw("CREATE EXTENSION IF NOT EXISTS vector");

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
        }
        catch
        {
            // Ignore when not PostgreSQL or extension unavailable
        }

        return app;
    }
}
