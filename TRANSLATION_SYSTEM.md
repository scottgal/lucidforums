# Translation System

## Overview

LucidForums features a comprehensive multi-language translation system with AI-powered translation, efficient HTMX-based language switching, and three-tier caching for optimal performance.

## Key Features

### 1. HTMX Out-of-Band (OOB) Swaps
- Efficient partial page updates when switching languages
- No full page reload required
- Only visible elements are updated
- 10x smaller payloads compared to JSON approach

### 2. Three-Tier Caching Strategy
1. **Request-scoped cache** (`RequestTranslationCache`): Prevents concurrent DbContext access within a single HTTP request
2. **Memory cache** (`IMemoryCache`): 1-hour TTL for frequently accessed translations
3. **Database**: PostgreSQL with indexed translation tables

### 3. AI-Powered Translation
- Automatic translation of UI strings and user-generated content
- Uses configured AI provider (Ollama/LM Studio)
- Preserves formatting, placeholders, and HTML tags
- Tracks translation source and staleness

## Architecture

### Database Schema

**TranslationStrings**: Base translatable strings
- `Key`: Unique identifier (e.g., "home.welcome.title")
- `DefaultText`: English text
- `Category`: Optional categorization
- `Context`: Optional context for translators

**Translations**: Language-specific translations
- `TranslationStringId`: Link to base string
- `LanguageCode`: ISO language code (e.g., "es", "fr", "ja")
- `TranslatedText`: Translated content
- `Source`: Manual/AI-generated/Imported
- `AiModel`: AI model used for translation

**ContentTranslations**: User-generated content translations
- `ContentType`: "Forum", "Thread", "Message"
- `ContentId`: ID of the content
- `FieldName`: Which field is translated
- `LanguageCode`: Target language
- `SourceHash`: Detects when source changed (staleness)

### Tag Helper Usage

Wrap any translatable text in `<t>` tags:

```html
<h1>
    <t key="home.welcome.title">Welcome to <span class="text-primary">LucidForums</span></t>
</h1>

<p>
    <t key="home.welcome.subtitle">An experimental intelligent forum platform from MostlyLucid</t>
</p>
```

The tag helper:
- Generates deterministic IDs using SHA256 content hash (e.g., `t-3b324de381bac2b6`)
- Adds `data-translate-key` for JavaScript access
- Adds `data-content-hash` for content verification
- Auto-creates translation strings in database
- Retrieves cached translations efficiently

### Language Switching

**Client-side** (`wwwroot/js/translation.js`):
1. User selects language from dropdown
2. JavaScript collects all `data-translate-key` attributes on page
3. Sends POST to `/Language/Switch/{languageCode}` with all keys
4. Receives HTMX OOB swap HTML
5. Updates each element's innerHTML
6. Sets cookie for language persistence

**Server-side** (`Controllers/LanguageController.cs`):
1. Sets `preferred-language` cookie (1-year expiration)
2. Fetches translations for all requested keys
3. Generates HTMX OOB swap elements:
   ```html
   <span id="t-{hash}" hx-swap-oob="innerHTML">Translated text</span>
   ```
4. Returns concatenated HTML

### Translation Service

**TranslationService** (`Services/Translation/TranslationService.cs`):

Key methods:
- `GetAsync(key, languageCode)`: Get translation with three-tier caching
- `EnsureStringAsync(key, defaultText)`: Create/update translation string
- `TranslateAsync(text, targetLanguage)`: AI-powered translation
- `TranslateAllStringsAsync(targetLanguage)`: Bulk translation with progress reporting
- `GetAvailableLanguagesAsync()`: List supported languages
- `GetStatsAsync(languageCode)`: Translation completion statistics

**Concurrency Fix**:
The service uses:
1. `SemaphoreSlim` to serialize database access
2. `AsNoTracking()` queries to avoid DbContext change tracker
3. Split queries to prevent navigation property tracking issues
4. Request-scoped cache to avoid redundant queries in same request

## Admin UI

Navigate to **Admin → Translation** to:
- View all translation strings
- Select target language
- Trigger AI translation with real-time progress
- View completion statistics
- Manage translation strings

## Usage Examples

### Add Translatable Text to View

```html
<!-- Simple text -->
<h2><t key="section.title">Section Title</t></h2>

<!-- With HTML formatting -->
<p>
    <t key="section.description">
        This is a <strong>formatted</strong> description with <a href="#">links</a>.
    </t>
</p>

<!-- With context (helps translators) -->
<button>
    <t key="button.submit" context="Form submission button">Submit</t>
</button>
```

### Programmatic Translation

```csharp
// In controller or service
private readonly ITranslationService _translation;

// Get translation
var welcomeText = await _translation.GetAsync("home.welcome.title", "es");

// Ensure string exists
await _translation.EnsureStringAsync(
    key: "new.key",
    defaultText: "Default English text",
    category: "ui",
    context: "Displayed on homepage"
);

// AI translate
var translated = await _translation.TranslateAsync(
    text: "Hello, world!",
    targetLanguage: "fr",
    sourceLanguage: "en"
);
```

### Bulk Translation

```csharp
var progress = new Progress<TranslationProgress>(p =>
{
    Console.WriteLine($"Translating {p.CurrentKey}: {p.Completed}/{p.Total}");
});

var translatedCount = await _translation.TranslateAllStringsAsync(
    targetLanguage: "es",
    overwriteExisting: false,
    progress: progress
);
```

## Performance Characteristics

### Cache Hit Rates
1. **Request cache**: ~100% for multiple uses in same request (e.g., layout + page)
2. **Memory cache**: ~95% for frequently accessed UI strings
3. **Database**: Only on cache miss or first request

### Language Switch Performance
- **Time**: ~50-200ms depending on element count
- **Payload size**: ~2-10KB for typical page (vs 100KB+ for JSON approach)
- **Network requests**: 1 POST request
- **DOM updates**: Direct innerHTML replacement (no parsing)

### Concurrent Request Handling
- Multiple tag helpers on same page: ✅ Works (request cache)
- Multiple simultaneous users: ✅ Works (scoped DbContext)
- Parallel translation jobs: ✅ Works (semaphore locking)

## Future Enhancements

- [ ] Translation memory for consistency
- [ ] Machine translation quality scoring
- [ ] Translator portal for community translations
- [ ] A/B testing for translation variants
- [ ] Integration with translation services (Google Translate, DeepL)
- [ ] Pluralization support
- [ ] Variable interpolation (e.g., "Welcome, {name}!")
- [ ] Right-to-left (RTL) language support
