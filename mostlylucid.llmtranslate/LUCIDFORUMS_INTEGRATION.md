# Integrating AutoTranslate into LucidForums

This guide shows how to integrate the `LucidForums.AutoTranslate` package into the main LucidForums project.

## Step 1: Add Project Reference

Add a reference to the AutoTranslate project in your `LucidForums.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\LucidForums.AutoTranslate\LucidForums.AutoTranslate.csproj" />
</ItemGroup>
```

Or install via NuGet once published:

```bash
dotnet add package LucidForums.AutoTranslate
```

## Step 2: Create AI Provider Adapter

Create a new file `Services/Translation/LucidForumsAiProviderAdapter.cs`:

```csharp
using System.Text.Json;
using LucidForums.AutoTranslate.Services;
using LucidForums.Models.Entities;
using LucidForums.Services.Ai;

namespace LucidForums.Services.Translation;

/// <summary>
/// Adapter that bridges LucidForums' ITextAiService to IAiTranslationProvider
/// </summary>
public class LucidForumsAiProviderAdapter : IAiTranslationProvider
{
    private readonly ITextAiService _aiService;
    private readonly ILogger<LucidForumsAiProviderAdapter> _logger;

    public LucidForumsAiProviderAdapter(
        ITextAiService aiService,
        ILogger<LucidForumsAiProviderAdapter> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<string> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = "en",
        CancellationToken ct = default)
    {
        try
        {
            return await _aiService.TranslateAsync(text, targetLanguage, sourceLanguage, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to translate text to {Language}", targetLanguage);
            return text;
        }
    }

    public async Task<Dictionary<string, string>> TranslateBatchAsync(
        Dictionary<string, string> items,
        string targetLanguage,
        string? sourceLanguage = "en",
        CancellationToken ct = default)
    {
        try
        {
            var charter = new Charter
            {
                Name = "BatchTranslation",
                Purpose = $"Translate UI strings from {sourceLanguage ?? "en"} to {targetLanguage}"
            };

            var jsonPayload = JsonSerializer.Serialize(
                items.Select(kvp => new { key = kvp.Key, text = kvp.Value }).ToList()
            );

            var prompt = $@"Translate these UI strings from {sourceLanguage ?? "en"} to {targetLanguage}.
Preserve HTML, placeholders. Respond with ONLY a JSON array: [{{""key"":""..."", ""translated"":""...""}}]

{jsonPayload}";

            var aiResult = await _aiService.GenerateAsync(charter, prompt, ct: ct);
            return ParseBatchResponse(aiResult);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch translation failed");
            var result = new Dictionary<string, string>();
            foreach (var (key, text) in items)
            {
                try
                {
                    result[key] = await _aiService.TranslateAsync(text, targetLanguage, sourceLanguage, ct);
                }
                catch { result[key] = text; }
            }
            return result;
        }
    }

    private Dictionary<string, string> ParseBatchResponse(string? aiResult)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(aiResult)) return result;

        try
        {
            var items = JsonSerializer.Deserialize<List<BatchItem>>(aiResult,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item.Key))
                        result[item.Key] = item.Translated ?? string.Empty;
                }
            }
        }
        catch { /* Return empty */ }

        return result;
    }

    private sealed class BatchItem
    {
        public string? Key { get; set; }
        public string? Translated { get; set; }
    }
}
```

## Step 3: Update ApplicationDbContext

Make your `ApplicationDbContext` implement `ITranslationDbContext`:

```csharp
using LucidForums.AutoTranslate.Data;
using LucidForums.AutoTranslate.Models;

namespace LucidForums.Data;

public class ApplicationDbContext : IdentityDbContext<User>, ITranslationDbContext
{
    // ... existing code ...

    // Add these DbSets (they're already there, just add the interface)
    public DbSet<TranslationString> TranslationStrings => Set<TranslationString>();
    public DbSet<AutoTranslate.Models.Translation> Translations => Set<AutoTranslate.Models.Translation>();

    // Note: You may need to alias if there's a naming conflict with existing Translation entity
    // public DbSet<AutoTranslate.Models.Translation> AutoTranslations => Set<AutoTranslate.Models.Translation>();

    // ... rest of existing code ...
}
```

**Important**: There's currently a `Translation` entity in LucidForums. You have two options:

### Option A: Rename Existing Translation Entity

Rename `LucidForums.Models.Entities.Translation` to something like `ContentTranslation` (which already exists!) and update references.

### Option B: Use Type Aliases

Use type aliasing in files that need both:

```csharp
using LucidTranslation = LucidForums.Models.Entities.Translation;
using AutoTranslation = LucidForums.AutoTranslate.Models.Translation;
```

## Step 4: Update ServiceCollectionExtensions.cs

Replace the existing translation service registration with AutoTranslate:

```csharp
using LucidForums.AutoTranslate.Extensions;
using LucidForums.AutoTranslate.Services;
using LucidForums.Services.Translation;

// In your AddLucidForumsServices or similar method:

public static IServiceCollection AddLucidForumsTranslation(this IServiceCollection services)
{
    // Register the adapter
    services.AddScoped<IAiTranslationProvider, LucidForumsAiProviderAdapter>();

    // Register AutoTranslate with existing DbContext
    services.AddAutoTranslateWithExistingDbContext<ApplicationDbContext>();

    return services;
}
```

Then call it in your main registration:

```csharp
services
    .AddLucidForumsMvcAndRealtime()
    .AddLucidForumsConfiguration()
    .AddLucidForumsDatabase()
    .AddLucidForumsAi()
    .AddLucidForumsTranslation()  // <-- Add this line
    // ... rest of registrations
```

## Step 5: Update Program.cs

Add the SignalR hub mapping:

```csharp
using LucidForums.AutoTranslate.Extensions;

// After app.MapHub<ForumHub>("/hubs/forum");
app.MapAutoTranslateHub(); // Adds /hubs/translation
```

## Step 6: Copy JavaScript File

Copy the JavaScript file to your wwwroot:

```bash
cp LucidForums.AutoTranslate/wwwroot/js/translation.js LucidForums/wwwroot/js/
```

Or if using the NuGet package, the file will be in the package's `contentFiles` folder.

## Step 7: Update _Layout.cshtml

Add the script reference:

```html
<!-- Existing scripts -->
<script src="~/js/bundle.js" asp-append-version="true"></script>

<!-- Add translation system -->
<script src="~/js/translation.js" asp-append-version="true"></script>
```

## Step 8: Update _ViewImports.cshtml

Add the AutoTranslate tag helpers:

```cshtml
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@addTagHelper *, LucidForums
@addTagHelper *, LucidForums.AutoTranslate  @* <-- Add this line *@
```

## Step 9: Remove Old Translation Code

You can now remove or deprecate these files (after verifying everything works):

- `LucidForums/Services/Translation/TranslationService.cs` (replaced by package)
- `LucidForums/Services/Translation/RequestTranslationCache.cs` (replaced by package)
- `LucidForums/Services/Translation/PageLanguageSwitchService.cs` (replaced by package)
- `LucidForums/TagHelpers/AutoTranslateTagHelper.cs` (replaced by package)
- `LucidForums/TagHelpers/TranslateTagHelper.cs` (replaced by package)
- `LucidForums/Helpers/TranslationHelper.cs` (replaced by package)
- `LucidForums/Hubs/TranslationHub.cs` (replaced by package)
- `LucidForums/Controllers/LanguageController.cs` (replaced by package)
- `LucidForums/wwwroot/js/translation.js` (replaced by package version)

Keep these (specific to LucidForums content translation):
- `LucidForums/Services/Translation/ContentTranslationService.cs`
- `LucidForums/Services/Translation/ContentTranslationQueue.cs`
- `LucidForums/Services/Translation/ContentTranslationHostedService.cs`
- `LucidForums/Controllers/AdminTranslationController.cs`

## Step 10: Run Migration

Create a migration to add the translation tables (they should already exist, but EF needs to know about the AutoTranslate models):

```bash
dotnet ef migrations add IntegrateAutoTranslate
dotnet ef database update
```

If you get conflicts because tables already exist, you may need to:

1. Create an empty migration and manually edit it to do nothing
2. Or, use `[Table("existing_name")]` attributes to map to existing tables

## Step 11: Test

1. Start the application
2. Visit a page with `auto-translate="true"` elements
3. Open browser console and check for "Translation system initialized"
4. Try switching languages using your language switcher UI
5. Verify translations appear and are stored in the database

## Troubleshooting

### Naming Conflicts

If you get naming conflicts with the existing `Translation` entity:

1. In `ApplicationDbContext.OnModelCreating`, configure table names:

```csharp
modelBuilder.Entity<AutoTranslate.Models.Translation>(e =>
{
    e.ToTable("Translations");
    // ... configuration
});

modelBuilder.Entity<LucidForums.Models.Entities.Translation>(e =>
{
    e.ToTable("ContentTranslations");
    // ... configuration
});
```

2. Update existing migration to rename table if needed

### Missing Translations

If translations aren't appearing:

1. Check browser console for JavaScript errors
2. Check server logs for translation service errors
3. Verify SignalR connection: open dev tools → Network → WS/WebSocket
4. Verify cookie `preferred-language` is being set

### JavaScript Not Loading

If `setLanguage()` is undefined:

1. Verify script is included in layout
2. Check browser console for 404 errors
3. Verify file exists in `wwwroot/js/translation.js`

## Benefits of Using the Package

- **Decoupled**: Translation logic is separate from forum-specific code
- **Reusable**: Can use in other ASP.NET Core projects
- **Maintained**: Updates to translation system don't require changes to LucidForums
- **Testable**: Easier to test translation logic in isolation
- **NuGet Ready**: Can publish and share with community

## Migration Path

If you want to gradually migrate:

1. Keep old services alongside new ones with different namespaces
2. Use feature flags to switch between implementations
3. Once verified, remove old code

The package is designed to be a drop-in replacement for the existing LucidForums translation system.
