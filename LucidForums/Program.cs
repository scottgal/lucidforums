using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

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

// Moderation
builder.Services.AddSingleton<LucidForums.Services.Moderation.IModerationService, LucidForums.Services.Moderation.ModerationService>();

// Forum domain services
builder.Services.AddScoped<LucidForums.Services.Forum.IForumService, LucidForums.Services.Forum.ForumService>();
builder.Services.AddScoped<LucidForums.Services.Forum.IThreadService, LucidForums.Services.Forum.ThreadService>();
builder.Services.AddScoped<LucidForums.Services.Forum.IMessageService, LucidForums.Services.Forum.MessageService>();
builder.Services.AddScoped<LucidForums.Services.Forum.IThreadViewService, LucidForums.Services.Forum.ThreadViewService>();

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

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages();

// Ensure database exists
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LucidForums.Data.ApplicationDbContext>();
    db.Database.EnsureCreated();
}

app.Run();