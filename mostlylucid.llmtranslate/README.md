# LLM Translation System

**Note**: This project will be renamed to `mostlylucid.llmtranslate` - an open-source AI-powered translation system for ASP.NET Core from [mostlylucid.net](https://www.mostlylucid.net).

Auto-translation tag helpers for ASP.NET Core with HTMX integration and AI-powered translations. Automatically translates UI strings and user content with real-time updates via SignalR.

## Features

- **Multiple Storage Options**: PostgreSQL (with schema support), SQLite, or JSON file storage
- **Tag Helper Integration**: Automatically translate any HTML element or use manual translation keys
- **HTMX OOB Swaps**: Efficient language switching without page reloads
- **AI-Powered Translation**: Pluggable AI translation provider interface
- **Real-time Updates**: SignalR integration for live translation progress
- **UI Components**: Built-in language selector and status display tag helpers
- **Multi-level Caching**: Request-scoped, memory cache, and persistent storage
- **Deterministic IDs**: Hash-based element IDs for reliable HTMX targeting

## Installation

```bash
dotnet add package LucidForums.AutoTranslate
```

## Storage Configuration

### Option 1: PostgreSQL (Recommended for Production)

**With default schema (public):**

```csharp
builder.Services.AddAutoTranslateWithPostgreSql(
    builder.Configuration.GetConnectionString("DefaultConnection")
);
```

**With custom schema:**

```csharp
builder.Services.AddAutoTranslateWithPostgreSql(
    connectionString: builder.Configuration.GetConnectionString("DefaultConnection"),
    schema: "translations"  // Custom schema name
);
```

**Using configuration options:**

```csharp
builder.Services.AddAutoTranslate(options =>
{
    options.StorageType = TranslationStorageType.PostgreSql;
    options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.PostgreSqlSchema = "translations"; // Optional, defaults to "public"
    options.EnableMemoryCache = true;
    options.MemoryCacheDurationMinutes = 60;
});
```

**Connection string example:**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=myapp;Username=postgres;Password=secret"
  }
}
```

**Run migrations:**

```bash
dotnet ef migrations add InitialTranslation --context TranslationDbContext
dotnet ef database update --context TranslationDbContext
```

### Option 2: SQLite (Great for Development/Small Apps)

**Basic setup:**

```csharp
builder.Services.AddAutoTranslateWithSqlite(
    "Data Source=translations.db"
);
```

**With configuration options:**

```csharp
builder.Services.AddAutoTranslate(options =>
{
    options.StorageType = TranslationStorageType.Sqlite;
    options.ConnectionString = "Data Source=translations.db;Cache=Shared";
    options.EnableMemoryCache = true;
    options.MemoryCacheDurationMinutes = 120; // Longer cache for file-based DB
});
```

**Run migrations:**

```bash
dotnet ef migrations add InitialTranslation --context TranslationDbContext
dotnet ef database update --context TranslationDbContext
```

### Option 3: JSON File (Simple/Portable)

Perfect for:
- Static site generation
- Version control of translations
- Simple deployments
- Development/testing

```csharp
builder.Services.AddAutoTranslateWithJsonFile(
    filePath: "App_Data/translations.json",
    autoSave: true  // Auto-save changes immediately
);
```

**With configuration options:**

```csharp
builder.Services.AddAutoTranslate(options =>
{
    options.StorageType = TranslationStorageType.JsonFile;
    options.JsonFilePath = Path.Combine(builder.Environment.ContentRootPath, "translations.json");
    options.JsonAutoSave = true; // Save after each change
    options.EnableMemoryCache = true;
    options.MemoryCacheDurationMinutes = 30;
});
```

**JSON file structure:**

```json
{
  "strings": [
    {
      "id": 1,
      "key": "home.welcome",
      "defaultText": "Welcome to our site",
      "category": "Home",
      "context": "Main page greeting",
      "translations": [
        {
          "id": 1,
          "languageCode": "es",
          "translatedText": "Bienvenido a nuestro sitio",
          "source": 1,
          "aiModel": "gpt-4"
        },
        {
          "id": 2,
          "languageCode": "fr",
          "translatedText": "Bienvenue sur notre site",
          "source": 1
        }
      ]
    }
  ]
}
```

### Option 4: Existing DbContext

If you already have a DbContext, make it implement `ITranslationDbContext`:

```csharp
public class ApplicationDbContext : DbContext, ITranslationDbContext
{
    public DbSet<TranslationString> TranslationStrings => Set<TranslationString>();
    public DbSet<Translation> Translations => Set<Translation>();

    // ... your other DbSets
}
```

Then register:

```csharp
builder.Services.AddAutoTranslateWithExistingDbContext<ApplicationDbContext>();
```

## Built-in AI Translation Providers

The package ships with ready-to-use providers. Pick one, add minimal configuration, and register it for IAiTranslationProvider.

Supported built-ins:
- OpenAI (hosted)
- Azure OpenAI (Azure AI) Chat Completions
- Ollama (local LLM)
- LM Studio (local, OpenAI-compatible)

General registration pattern (Program.cs):

```csharp
// Select ONE provider registration below
builder.Services.AddAutoTranslateWithSqlite("Data Source=translations.db");

var cfg = builder.Configuration;
var httpFactory = builder.Services.AddHttpClient(); // ensures IHttpClientFactory is available
```

Notes:
- For security, store secrets in User Secrets or environment variables, not in source control.
- Each example below shows appsettings.json plus DI registration using IHttpClientFactory.

### OpenAI (hosted)

appsettings.json:
```json
{
  "OpenAI": {
    "ApiKey": "${OPENAI_API_KEY}",
    "Model": "gpt-4o-mini"
  }
}
```

Note: Use a model suited for translation such as gpt-4o-mini, gpt-4o, or o3-mini.

Environment variables (alternative):
- OPENAI_API_KEY
- OPENAI_MODEL (optional)

Program.cs registration:
```csharp
builder.Services.AddHttpClient("OpenAI", c => c.BaseAddress = new Uri("https://api.openai.com/"));
builder.Services.AddScoped<IAiTranslationProvider>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAI");
    var logger = sp.GetRequiredService<ILogger<mostlylucid.llmtranslate.Services.Providers.OpenAiTranslationProvider>>();
    var apiKey = cfg["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
    var model = cfg["OpenAI:Model"] ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
    return new mostlylucid.llmtranslate.Services.Providers.OpenAiTranslationProvider(http, logger, apiKey, model);
});
```

### Azure OpenAI (Azure AI)

You need your resource endpoint, deployment name, and an API key. The provider defaults api-version to 2024-06-01; override if needed.

appsettings.json:
```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com",
    "ApiKey": "${AZURE_OPENAI_API_KEY}",
    "Deployment": "gpt-4o-mini",
    "ApiVersion": "2024-06-01"
  }
}
```

Environment variables (alternative):
- AZURE_OPENAI_ENDPOINT
- AZURE_OPENAI_API_KEY
- AZURE_OPENAI_DEPLOYMENT
- AZURE_OPENAI_API_VERSION (optional)

Program.cs registration:
```csharp
builder.Services.AddHttpClient("AzureOpenAI");
builder.Services.AddScoped<IAiTranslationProvider>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("AzureOpenAI");
    var logger = sp.GetRequiredService<ILogger<mostlylucid.llmtranslate.Services.Providers.AzureAiTranslationProvider>>();
    var endpoint = cfg["AzureOpenAI:Endpoint"] ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!;
    var apiKey = cfg["AzureOpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!;
    var deployment = cfg["AzureOpenAI:Deployment"] ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")!;
    var apiVersion = cfg["AzureOpenAI:ApiVersion"] ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? "2024-06-01";
    return new mostlylucid.llmtranslate.Services.Providers.AzureAiTranslationProvider(http, logger, endpoint, apiKey, deployment, apiVersion);
});
```

### Ollama (local)

Ensure Ollama is running and a model is pulled (e.g., llama3.2). Default base URL is http://localhost:11434/.

appsettings.json:
```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434/",
    "Model": "llama3.2"
  }
}
```

Environment variables (alternative):
- OLLAMA_BASE_URL (optional, defaults to http://localhost:11434/)
- OLLAMA_MODEL (optional, defaults to llama3.2)

Program.cs registration:
```csharp
builder.Services.AddHttpClient("Ollama", c => c.BaseAddress = new Uri(cfg["Ollama:BaseUrl"] ?? Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434/"));
builder.Services.AddScoped<IAiTranslationProvider>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Ollama");
    var logger = sp.GetRequiredService<ILogger<mostlylucid.llmtranslate.Services.Providers.OllamaTranslationProvider>>();
    var model = cfg["Ollama:Model"] ?? Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama3.2";
    var baseUrl = cfg["Ollama:BaseUrl"] ?? Environment.GetEnvironmentVariable("OLLAMA_BASE_URL");
    return new mostlylucid.llmtranslate.Services.Providers.OllamaTranslationProvider(http, logger, model, baseUrl);
});
```

### LM Studio (local, OpenAI-compatible)

LM Studio exposes an OpenAI-like API (default http://localhost:1234/v1). Choose a local model you have loaded.

appsettings.json:
```json
{
  "LmStudio": {
    "BaseUrl": "http://localhost:1234/",
    "Model": "qwen2.5:latest"
  }
}
```

Environment variables (alternative):
- LMSTUDIO_BASE_URL (optional, defaults to http://localhost:1234/)
- LMSTUDIO_MODEL (optional, defaults to qwen2.5:latest)

Program.cs registration:
```csharp
builder.Services.AddHttpClient("LmStudio", c => c.BaseAddress = new Uri(cfg["LmStudio:BaseUrl"] ?? Environment.GetEnvironmentVariable("LMSTUDIO_BASE_URL") ?? "http://localhost:1234/"));
builder.Services.AddScoped<IAiTranslationProvider>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("LmStudio");
    var logger = sp.GetRequiredService<ILogger<mostlylucid.llmtranslate.Services.Providers.LmStudioTranslationProvider>>();
    var model = cfg["LmStudio:Model"] ?? Environment.GetEnvironmentVariable("LMSTUDIO_MODEL") ?? "qwen2.5:latest";
    var baseUrl = cfg["LmStudio:BaseUrl"] ?? Environment.GetEnvironmentVariable("LMSTUDIO_BASE_URL");
    return new mostlylucid.llmtranslate.Services.Providers.LmStudioTranslationProvider(http, logger, model, baseUrl);
});
```

Switching providers: register only one IAiTranslationProvider at a time, or use named registrations and a small adapter to pick one via config.

Provider behavior notes:
- All providers support single and batch translation; if batch JSON parsing fails, they fall back to per-item translation.
- Prompts preserve placeholders like {0} and {name}, and HTML tags; still validate outputs for your UI.

## AI Provider Implementation

You must implement `IAiTranslationProvider` to connect your AI backend:

### Example: OpenAI

```csharp
public class OpenAiTranslationProvider : IAiTranslationProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public async Task<string> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = "en",
        CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", new
        {
            model = "gpt-4",
            messages = new[]
            {
                new { role = "system", content = $"Translate from {sourceLanguage} to {targetLanguage}" },
                new { role = "user", content = text }
            }
        }, ct);

        var result = await response.Content.ReadFromJsonAsync<OpenAiResponse>(ct);
        return result.Choices[0].Message.Content;
    }

    public async Task<Dictionary<string, string>> TranslateBatchAsync(
        Dictionary<string, string> items,
        string targetLanguage,
        string? sourceLanguage = "en",
        CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(items.Select(kvp => new { key = kvp.Key, text = kvp.Value }));

        var prompt = $@"Translate these strings from {sourceLanguage} to {targetLanguage}.
Return ONLY a JSON array: [{{""key"":""..."", ""translated"":""...""}}]

{json}";

        var translated = await TranslateAsync(prompt, targetLanguage, sourceLanguage, ct);
        var result = JsonSerializer.Deserialize<List<BatchItem>>(translated);

        return result.ToDictionary(x => x.Key, x => x.Translated);
    }
}
```

### Example: Ollama (Local LLM)

```csharp
public class OllamaTranslationProvider : IAiTranslationProvider
{
    private readonly HttpClient _http;

    public async Task<string> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = "en",
        CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("http://localhost:11434/api/generate", new
        {
            model = "llama3.2",
            prompt = $"Translate from {sourceLanguage} to {targetLanguage}: {text}",
            stream = false
        }, ct);

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(ct);
        return result.Response;
    }

    // ... implement TranslateBatchAsync similarly
}
```

**Register your provider:**

```csharp
builder.Services.AddScoped<IAiTranslationProvider, OpenAiTranslationProvider>();
```

## Application Configuration

```csharp
// In Program.cs, configure services
builder.Services.AddAutoTranslateWithSqlite("Data Source=translations.db");
builder.Services.AddScoped<IAiTranslationProvider, YourAiProvider>();

// Map the SignalR hub
app.MapAutoTranslateHub(); // Default: /hubs/translation

// Map controllers (if not already done)
app.MapControllers();
```

## View Configuration

Add to `_ViewImports.cshtml`:

```cshtml
@addTagHelper *, LucidForums.AutoTranslate
```

## Usage

### 1. Auto-Translate Tag Helper

Automatically translates any HTML element:

```html
<h1 auto-translate="true">Welcome to My App</h1>
<p auto-translate="true" translation-category="home">
    This text will be automatically translated
</p>
<button auto-translate="true" translation-category="nav">
    Click Me
</button>
```

### 2. Manual Translation Tag Helper

Use explicit translation keys:

```html
<t key="home.welcome">Welcome</t>
<t key="nav.home" category="navigation">Home</t>
<t key="greeting" lang="es">Hello</t>
```

### 3. Language Selector

Add a language selector dropdown:

```html
<!-- Simple dropdown -->
<language-selector style="dropdown" />

<!-- With custom languages -->
<language-selector
    style="dropdown"
    languages="en,es,fr,de,ja"
    show-names="true"
    show-codes="true" />

<!-- Button style -->
<language-selector style="buttons" class="my-custom-class" />

<!-- Select box -->
<language-selector style="select" />

<!-- Flag icons (requires flag-icon-css) -->
<language-selector style="flags" show-names="true" />
```

**Selector styles:**
- `dropdown` - Bootstrap dropdown (default)
- `buttons` - Button group
- `select` - HTML select element
- `flags` - Flag icons with names

### 4. Translation Status Display

Show current language and translation progress:

```html
<!-- Top-right corner (default) -->
<translation-status />

<!-- With detailed progress -->
<translation-status position="top-right" show-details="true" />

<!-- Different positions -->
<translation-status position="bottom-left" />
<translation-status position="inline" />
```

**Positions:**
- `top-right` (default)
- `top-left`
- `bottom-right`
- `bottom-left`
- `inline`

### 5. Include Scripts

Add to your layout (before closing `</body>` tag):

```html
<!-- Basic (includes SignalR from CDN) -->
<translation-scripts />

<!-- With configuration -->
<translation-scripts
    signalr-hub="/hubs/translation"
    include-signalr="true"
    debug="false"
    enable-notifications="true" />

<!-- Without SignalR (no real-time updates) -->
<translation-scripts include-signalr="false" />

<!-- Debug mode -->
<translation-scripts debug="true" />
```

## Complete Layout Example

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>My App</title>
    <link rel="stylesheet" href="~/css/site.css" />
</head>
<body>
    <header>
        <nav>
            <t key="nav.home">Home</t>
            <t key="nav.about">About</t>
            <language-selector style="dropdown" languages="en,es,fr,de,ja" />
        </nav>
    </header>

    <main>
        @RenderBody()
    </main>

    <footer>
        <p auto-translate="true" translation-category="footer">
            © 2025 My Company. All rights reserved.
        </p>
    </footer>

    <!-- Translation status indicator -->
    <translation-status position="top-right" show-details="true" />

    <!-- Translation scripts (must be at end of body) -->
    <translation-scripts
        signalr-hub="/hubs/translation"
        debug="@(Environment.IsDevelopment())" />
</body>
</html>
```

## Configuration Matrix

| Storage Type | Best For | Pros | Cons |
|---|---|---|---|
| **PostgreSQL** | Production, high traffic | Best performance, ACID, scalability, schema isolation | Requires PostgreSQL server |
| **SQLite** | Small apps, development | Simple setup, file-based, portable | Not for high concurrency |
| **JSON File** | Static sites, dev, version control | Human-readable, portable, no DB required | Slower for large datasets |

## Advanced Configuration

### Custom Cache Settings

```csharp
builder.Services.AddAutoTranslate(options =>
{
    options.StorageType = TranslationStorageType.PostgreSql;
    options.ConnectionString = "...";
    options.EnableMemoryCache = true;
    options.MemoryCacheDurationMinutes = 120; // 2 hours
});
```

### Multiple Languages Configuration

```csharp
// In appsettings.json
{
  "Translation": {
    "SupportedLanguages": ["en", "es", "fr", "de", "ja", "zh", "ar"],
    "DefaultLanguage": "en"
  }
}
```

### Environment-Specific Storage

```csharp
if (builder.Environment.IsDevelopment())
{
    // Use JSON file in development
    builder.Services.AddAutoTranslateWithJsonFile("translations.json");
}
else
{
    // Use PostgreSQL in production
    builder.Services.AddAutoTranslateWithPostgreSql(
        builder.Configuration.GetConnectionString("Production"),
        schema: "translations"
    );
}
```

## JavaScript API

The translation system exposes a global JavaScript API:

```javascript
// Switch language
window.setLanguage('es');

// Or use the manager directly
window.translationManager.switchLanguage('fr');

// Get current language
const lang = window.translationManager.getCurrentLanguage();

// Collect all translation keys on page
const keys = window.translationManager.collectTranslationKeys();
```

## Troubleshooting

### Translations not appearing

1. Check browser console for errors
2. Verify SignalR connection (Network tab → WS)
3. Check `preferred-language` cookie is set
4. Enable debug mode: `<translation-scripts debug="true" />`

### Database migrations failing

```bash
# For PostgreSQL with custom schema
dotnet ef migrations add Initial --context TranslationDbContext -- --schema translations

# For SQLite
dotnet ef migrations add Initial --context TranslationDbContext
```

### SignalR not connecting

1. Ensure `app.MapAutoTranslateHub()` is called
2. Check hub path matches: `<translation-scripts signalr-hub="/hubs/translation" />`
3. Verify SignalR CDN is accessible

## Performance Tips

1. **Use PostgreSQL for production** - best performance and concurrency
2. **Enable memory caching** - reduces database queries
3. **Implement batch translation** - faster than individual translations
4. **Use JSON file for static content** - version control friendly
5. **Set appropriate cache duration** - balance freshness vs performance

## Requirements

- .NET 9.0+
- ASP.NET Core
- One of: PostgreSQL, SQLite, or file system access
- SignalR (included in ASP.NET Core)

## License

MIT

## Author

mostlylucid.net - [https://www.mostlylucid.net](https://www.mostlylucid.net)

## Contributing

Contributions welcome! Please open an issue or PR.

## Support

- Documentation: This README
- GitHub Issues: [Report issues](https://github.com/lucidforums/LucidForums/issues)
- Website: [mostlylucid.net](https://www.mostlylucid.net)
