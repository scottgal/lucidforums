using LucidForums.Services.Charters;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;
// Add services to the container.
services.AddControllersWithViews();
services.AddRazorPages();
services.AddRazorComponents();
// Real-time updates via SignalR
builder.Services.AddSignalR();

// Add EF Core + Identity (PostgreSQL if provided; fallback to SQLite for local dev)
builder.Services.AddDbContext<LucidForums.Data.ApplicationDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("Default")
             ?? builder.Configuration["ConnectionStrings:Default"]
             ?? builder.Configuration.GetConnectionString("DefaultConnection")
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

builder.Services.AddIdentity<LucidForums.Models.Entities.User, Microsoft.AspNetCore.Identity.IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<LucidForums.Data.ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Bind AI and Ollama options from configuration and environment
builder.Services.Configure<LucidForums.Services.Ai.AiOptions>(builder.Configuration.GetSection("AI"));
builder.Services.Configure<LucidForums.Services.Llm.OllamaOptions>(builder.Configuration.GetSection("Ollama"));

// Register AI services (Microsoft.Extensions.AI-first, fallback to Ollama HTTP)
builder.Services.AddSingleton<LucidForums.Services.Llm.IOllamaEndpointProvider, LucidForums.Services.Llm.OllamaEndpointProvider>();
builder.Services.AddHttpClient("ollama", (sp, client) =>
{
    var ep = sp.GetRequiredService<LucidForums.Services.Llm.IOllamaEndpointProvider>();
    client.BaseAddress = ep.GetBaseAddress();
});

// Core AI layer
builder.Services.AddSingleton<LucidForums.Services.Ai.ITextAiService, LucidForums.Services.Ai.TextAiService>();
builder.Services.AddSingleton<LucidForums.Services.Ai.IImageAiService, LucidForums.Services.Ai.ImageAiService>();
// Adapter for legacy IOllamaChatService usages
builder.Services.AddSingleton<LucidForums.Services.Llm.IOllamaChatService, LucidForums.Services.Ai.OllamaChatAdapter>();

// Seeding (background forum generator)
builder.Services.AddSingleton<LucidForums.Services.Seeding.IForumSeedingQueue, LucidForums.Services.Seeding.ForumSeedingQueue>();
builder.Services.AddHostedService<LucidForums.Services.Seeding.ForumSeedingHostedService>();

// Moderation
builder.Services.AddSingleton<LucidForums.Services.Moderation.IModerationService, LucidForums.Services.Moderation.ModerationService>();

// Embeddings / Semantic search
builder.Services.Configure<LucidForums.Services.Search.EmbeddingOptions>(builder.Configuration.GetSection("Embedding"));
builder.Services.AddScoped<LucidForums.Services.Search.IEmbeddingService, LucidForums.Services.Search.EmbeddingService>();

// Forum domain services
builder.Services.AddScoped<LucidForums.Services.Forum.IForumService, LucidForums.Services.Forum.ForumService>();
builder.Services.AddScoped<LucidForums.Services.Forum.IThreadService, LucidForums.Services.Forum.ThreadService>();
builder.Services.AddScoped<LucidForums.Services.Forum.IMessageService, LucidForums.Services.Forum.MessageService>();
builder.Services.AddScoped<LucidForums.Services.Forum.IThreadViewService, LucidForums.Services.Forum.ThreadViewService>();
// Charter domain service
builder.Services.AddScoped<ICharterService, CharterService>();

// Mapster (mapping) configuration
LucidForums.Web.Mapping.MapsterRegistration.Register(Mapster.TypeAdapterConfig.GlobalSettings);
// Register source-generated mapper via reflection to avoid compile-time dependency on generated type
var mapperImpl = Type.GetType("LucidForums.Web.Mapping.AppMapper, LucidForums");
if (mapperImpl is not null)
{
    builder.Services.AddSingleton(typeof(LucidForums.Web.Mapping.IAppMapper), mapperImpl);
}
else
{
    // Fallback: register a dummy that will throw at runtime if generator didn't run
    builder.Services.AddSingleton<LucidForums.Web.Mapping.IAppMapper>(_ => throw new InvalidOperationException("Mapster source-generated mapper not found. Ensure Mapster.SourceGenerator is installed and the project is built."));
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// Map attribute-routed API controllers
app.MapControllers();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages();

// Map SignalR hubs
app.MapHub<LucidForums.Hubs.ForumHub>(LucidForums.Hubs.ForumHub.HubPath);
app.MapHub<LucidForums.Hubs.SeedingHub>(LucidForums.Hubs.SeedingHub.HubPath);

// Ensure database exists and vector extension/table
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LucidForums.Data.ApplicationDbContext>();
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
}

app.Run();