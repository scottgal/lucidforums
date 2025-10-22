using LucidForums.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Microsoft.AspNetCore.Identity;
using LucidForums.Models.Entities;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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
        app.MapHub<LucidForums.Hubs.TranslationHub>(LucidForums.Hubs.TranslationHub.HubPath);

        return app;
    }

    public static async Task<WebApplication> InitializeLucidForumsDatabase(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Ensure the database exists
        try
        {
            // Try to create the database if it doesn't exist
            var connectionString = db.Database.GetConnectionString();
            if (!string.IsNullOrEmpty(connectionString))
            {
                // Parse connection string to get database name
                var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
                var databaseName = builder.Database;

                // Connect to postgres database to create our database
                builder.Database = "postgres";
                var masterConnectionString = builder.ToString();

                using var masterConnection = new Npgsql.NpgsqlConnection(masterConnectionString);
                await masterConnection.OpenAsync();

                // Check if database exists
                using var checkCmd = masterConnection.CreateCommand();
                checkCmd.CommandText = $"SELECT 1 FROM pg_database WHERE datname = '{databaseName}'";
                var exists = await checkCmd.ExecuteScalarAsync();

                if (exists == null)
                {
                    // Create database
                    using var createCmd = masterConnection.CreateCommand();
                    createCmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
                    await createCmd.ExecuteNonQueryAsync();
                    Log.Logger.Information("Created database: {DatabaseName}", databaseName);
                }
            }
        }
        catch (Exception ex)
        {
            // Log but don't fail - maybe database already exists or we don't have permissions
            Log.Logger.Warning(ex, "Could not ensure database exists - it may already exist or permissions may be insufficient");
        }

        // Now run migrations on the actual database
        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            // If migrations fail, try EnsureCreated as fallback
            Log.Logger.Warning(ex, "Migration failed, attempting EnsureCreated");
            await db.Database.EnsureCreatedAsync();
        }
        try
        {
            // Enable required extensions (handled inside SQL to avoid noisy errors when lacking privileges)
            await db.Database.ExecuteSqlRawAsync(@"DO $$ BEGIN
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
            await db.Database.ExecuteSqlRawAsync(@"DO $$ BEGIN
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
            await db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS message_embeddings (
            message_id uuid PRIMARY KEY,
            forum_id uuid NOT NULL,
            thread_id uuid NOT NULL,
            content_hash text NOT NULL,
            embedding vector,
            created_at timestamptz NOT NULL DEFAULT now()
        )");

            // Create IVFFlat index if not exists (wrapped in DO block to avoid errors if operator class missing)
            await db.Database.ExecuteSqlRawAsync(@"DO $$ BEGIN
            CREATE INDEX IF NOT EXISTS idx_message_embeddings_ivfflat ON message_embeddings USING ivfflat (embedding vector_cosine_ops);
        EXCEPTION WHEN OTHERS THEN
            -- ignore if cannot create (e.g., extension not supporting ivfflat); sequential scan will be used
            NULL;
        END $$;");

            // Ensure AppSettings table exists (case-sensitive EF Core default) and has a single row with Id=1
            await db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ""AppSettings"" (
                ""Id"" integer PRIMARY KEY,
                ""GenerationProvider"" text NULL,
                ""GenerationModel"" text NULL,
                ""TranslationProvider"" text NULL,
                ""TranslationModel"" text NULL,
                ""EmbeddingProvider"" text NULL,
                ""EmbeddingModel"" text NULL
            )");
            // Insert default row if absent
            await db.Database.ExecuteSqlRawAsync(@"INSERT INTO ""AppSettings"" (""Id"", ""GenerationProvider"", ""GenerationModel"", ""TranslationProvider"", ""TranslationModel"", ""EmbeddingProvider"", ""EmbeddingModel"")
            VALUES (1, NULL, NULL, NULL, NULL, NULL, NULL)
            ON CONFLICT (""Id"") DO NOTHING;");
        }
        catch
        {
            // Ignore when not PostgreSQL or extension unavailable
        }

        // Ensure charter score columns exist (PostgreSQL only; safe no-op otherwise)
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='Threads') THEN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='Threads' AND column_name='CharterScore') THEN
                    EXECUTE 'ALTER TABLE ""Threads"" ADD COLUMN ""CharterScore"" double precision NULL';
                END IF;
            END IF;
            IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='Messages') THEN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='Messages' AND column_name='CharterScore') THEN
                    EXECUTE 'ALTER TABLE ""Messages"" ADD COLUMN ""CharterScore"" double precision NULL';
                END IF;
            END IF;
        EXCEPTION WHEN OTHERS THEN
            NULL;
        END $$;");
        }
        catch { }

        // Development-only: create an Administrator role and a dev admin user for local testing
        try
        {
            if (app.Environment.IsDevelopment())
            {
                var userManager = scope.ServiceProvider.GetService<UserManager<User>>();
                var roleManager = scope.ServiceProvider.GetService<RoleManager<IdentityRole>>();
                if (userManager is not null && roleManager is not null)
                {
                    var roleName = "Administrator";
                    // Create role if missing
                    var roleExists = roleManager.RoleExistsAsync(roleName).GetAwaiter().GetResult();
                    if (!roleExists)
                    {
                        var rres = roleManager.CreateAsync(new IdentityRole(roleName)).GetAwaiter().GetResult();
                        if (!rres.Succeeded)
                        {
                            Log.Logger.Warning("Failed to create role {Role}: {Errors}", roleName, string.Join(',', rres.Errors.Select(e => e.Description)));
                        }
                    }

                    var adminEmail = "devadmin@localhost";
                    var admin = userManager.FindByEmailAsync(adminEmail).GetAwaiter().GetResult();
                    if (admin is null)
                    {
                        admin = new User { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
                        var createRes = userManager.CreateAsync(admin, "Password123!").GetAwaiter().GetResult();
                        if (!createRes.Succeeded)
                        {
                            Log.Logger.Warning("Failed to create dev admin user: {Errors}", string.Join(',', createRes.Errors.Select(e => e.Description)));
                        }
                        else
                        {
                            var addRoleRes = userManager.AddToRoleAsync(admin, roleName).GetAwaiter().GetResult();
                            if (!addRoleRes.Succeeded)
                            {
                                Log.Logger.Warning("Failed to add dev admin to role: {Errors}", string.Join(',', addRoleRes.Errors.Select(e => e.Description)));
                            }
                        }
                    }
                    else
                    {
                        // Ensure user is in role
                        var inRole = userManager.IsInRoleAsync(admin, roleName).GetAwaiter().GetResult();
                        if (!inRole)
                        {
                            var addRoleRes = userManager.AddToRoleAsync(admin, roleName).GetAwaiter().GetResult();
                            if (!addRoleRes.Succeeded)
                            {
                                Log.Logger.Warning("Failed to add existing dev admin to role: {Errors}", string.Join(',', addRoleRes.Errors.Select(e => e.Description)));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Don't fail startup if seeding dev admin fails; just log
            Log.Logger.Warning(ex, "Failed to seed development admin account");
        }

        // Seed sample charters (only if none exist yet)
        try
        {
            var existingCharters = db.Charters.AsNoTracking().Take(1).ToList();
            if (existingCharters.Count == 0)
            {
                var samples = new List<LucidForums.Models.Entities.Charter>
                {
                    new() {
                        Name = "Friendly Hobbyists",
                        Purpose = "A welcoming space for enthusiasts to share experiences, tips, and projects.",
                        Rules = new() { "Be kind and welcoming", "No personal attacks", "Stay on topic", "No spam or self-promo without value" },
                        Behaviors = new() { "Encourage newcomers", "Share constructive feedback", "Celebrate wins", "Use clear, friendly language" }
                    },
                    new() {
                        Name = "Academic Research",
                        Purpose = "Serious, evidence-driven discussion with citations and methodological rigor.",
                        Rules = new() { "Cite credible sources", "No anecdotes as evidence", "Disclose conflicts of interest", "No sensational claims" },
                        Behaviors = new() { "Use formal tone", "Summarize findings", "Link to papers/datasets", "Be precise and concise" }
                    },
                    new() {
                        Name = "No‑Nonsense Support",
                        Purpose = "Direct, solution-focused Q&A to resolve issues quickly.",
                        Rules = new() { "Provide steps tried", "Include environment details", "No off-topic chatter", "Mark solutions when resolved" },
                        Behaviors = new() { "Answer with actionable steps", "Avoid filler text", "Request clarifications succinctly" }
                    },
                    new() {
                        Name = "Wholesome & Encouraging",
                        Purpose = "Positive, uplifting community emphasizing empathy and support.",
                        Rules = new() { "Assume good intent", "No sarcasm or snark", "Content warnings where relevant", "No negativity-only posts" },
                        Behaviors = new() { "Thank contributors", "Use inclusive language", "Offer encouragement", "Share helpful resources" }
                    },
                    new() {
                        Name = "Debate Club (Civility First)",
                        Purpose = "Structured debate with strong moderation and civility requirements.",
                        Rules = new() { "Attack ideas, not people", "Steelman opponent arguments", "Cite sources for claims", "Follow moderator prompts" },
                        Behaviors = new() { "Use calm, neutral tone", "Acknowledge counterpoints", "Keep paragraphs short", "Avoid rhetorical questions as attacks" }
                    },
                    new() {
                        Name = "Beginner‑Friendly Classroom",
                        Purpose = "Learning-focused forum for newcomers; questions are always welcome.",
                        Rules = new() { "No gatekeeping", "Explain jargon", "Be patient", "Provide examples" },
                        Behaviors = new() { "Use step-by-step guides", "Offer alternative approaches", "Link to primers", "Invite follow-up questions" }
                    },
                    new() {
                        Name = "Strict Moderation",
                        Purpose = "Tightly curated space with zero tolerance for low-effort content.",
                        Rules = new() { "No memes or low-effort posts", "Cite sources", "Stay 100% on-topic", "Follow formatting templates" },
                        Behaviors = new() { "Flag policy violations", "Format posts per template", "Summarize key points first" }
                    },
                    new() {
                        Name = "Casual Chit‑Chat",
                        Purpose = "Laid-back conversation, light humor allowed; keep it respectful.",
                        Rules = new() { "No harassment", "Tag spoilers/NSFW", "Avoid politics/religion unless topical" },
                        Behaviors = new() { "Use conversational tone", "Add light humor", "Invite others to join in" }
                    }
                };
                db.Charters.AddRange(samples);
                db.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            // Do not fail startup if seeding sample charters fails
            Serilog.Log.Logger.Warning(ex, "Failed to seed sample charters at startup");
        }

        return app;
    }
}
