using LucidForums.Data;
using LucidForums.Models.Entities;
using LucidForums.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LucidForums.Services.Translation;

public class TranslationService : ITranslationService
{
    private readonly ApplicationDbContext _db;
    private readonly ITextAiService _ai;
    private readonly IMemoryCache _cache;
    private readonly RequestTranslationCache _requestCache;
    private readonly ILogger<TranslationService> _logger;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public TranslationService(
        ApplicationDbContext db,
        ITextAiService ai,
        IMemoryCache cache,
        RequestTranslationCache requestCache,
        ILogger<TranslationService> logger)
    {
        _db = db;
        _ai = ai;
        _cache = cache;
        _requestCache = requestCache;
        _logger = logger;
    }

    public async Task<string> GetAsync(string key, string languageCode, CancellationToken ct = default)
    {
        // Try request-scoped cache first (fastest, avoids concurrent DB access)
        if (_requestCache.TryGet(languageCode, key, out var requestCached) && requestCached != null)
            return requestCached;

        // Try memory cache
        var cacheKey = $"trans:{languageCode}:{key}";
        if (_cache.TryGetValue<string>(cacheKey, out var cached))
        {
            _requestCache.Set(languageCode, key, cached!);
            return cached!;
        }

        // Need to hit database - use lock to prevent concurrent access
        await _dbLock.WaitAsync(ct);
        try
        {
            // Double-check cache after acquiring lock (another thread may have loaded it)
            if (_cache.TryGetValue<string>(cacheKey, out var cachedAfterLock))
            {
                _requestCache.Set(languageCode, key, cachedAfterLock!);
                return cachedAfterLock!;
            }

            // Get translation from database using AsNoTracking to avoid concurrency issues
            // Use split queries to avoid concurrent access
            var translationString = await _db.TranslationStrings
                .AsNoTracking()
                .Where(ts => ts.Key == key)
                .Select(ts => new { ts.DefaultText })
                .FirstOrDefaultAsync(ct);

            if (translationString == null)
            {
                _logger.LogWarning("Translation string not found for key: {Key}", key);
                return key; // Return key as fallback
            }

            // If requesting default language, return default text
            if (languageCode == "en")
            {
                _cache.Set(cacheKey, translationString.DefaultText, TimeSpan.FromHours(1));
                _requestCache.Set(languageCode, key, translationString.DefaultText);
                return translationString.DefaultText;
            }

            // Get the translation for this specific language
            var translation = await _db.Translations
                .AsNoTracking()
                .Where(t => t.TranslationString!.Key == key && t.LanguageCode == languageCode)
                .Select(t => t.TranslatedText)
                .FirstOrDefaultAsync(ct);

            var result = translation ?? translationString.DefaultText;
            _cache.Set(cacheKey, result, TimeSpan.FromHours(1));
            _requestCache.Set(languageCode, key, result);
            return result;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<int> EnsureStringAsync(string key, string defaultText, string? category = null, string? context = null, CancellationToken ct = default)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            var existing = await _db.TranslationStrings
                .AsNoTracking()
                .Where(ts => ts.Key == key)
                .Select(ts => new { ts.Id, ts.DefaultText })
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                // Update if default text changed
                if (existing.DefaultText != defaultText)
                {
                    var toUpdate = await _db.TranslationStrings.FindAsync(new object[] { existing.Id }, ct);
                    if (toUpdate != null)
                    {
                        toUpdate.DefaultText = defaultText;
                        toUpdate.UpdatedAtUtc = DateTime.UtcNow;
                        await _db.SaveChangesAsync(ct);
                    }
                }
                return existing.Id;
            }

            var newString = new TranslationString
            {
                Key = key,
                DefaultText = defaultText,
                Category = category,
                Context = context
            };

            _db.TranslationStrings.Add(newString);
            await _db.SaveChangesAsync(ct);
            return newString.Id;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = "en", CancellationToken ct = default)
    {
        // Create a charter for translation context
        var charter = new Charter
        {
            Name = "Translation",
            Purpose = $"Translate text from {sourceLanguage} to {targetLanguage} accurately and naturally"
        };

        var prompt = $@"Translate the following text from {sourceLanguage} to {targetLanguage}.
Preserve formatting, placeholders (like {{0}}, {{name}}), and HTML tags if present.
Return ONLY the translated text, no explanations.

Text to translate:
{text}";

        var translated = await _ai.GenerateAsync(charter, prompt, ct: ct);
        return translated?.Trim() ?? text;
    }

    public async Task<int> TranslateAllStringsAsync(string targetLanguage, bool overwriteExisting = false, IProgress<TranslationProgress>? progress = null, CancellationToken ct = default)
    {
        var strings = await _db.TranslationStrings
            .Include(ts => ts.Translations)
            .ToListAsync(ct);

        var total = strings.Count;
        var completed = 0;
        var translated = 0;

        foreach (var str in strings)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(new TranslationProgress(total, completed, str.Key));

            // Skip if translation exists and not overwriting
            var existing = str.Translations.FirstOrDefault(t => t.LanguageCode == targetLanguage);
            if (existing != null && !overwriteExisting)
            {
                completed++;
                continue;
            }

            try
            {
                var translatedText = await TranslateAsync(str.DefaultText, targetLanguage, "en", ct);

                if (existing != null)
                {
                    existing.TranslatedText = translatedText;
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                    existing.Source = TranslationSource.AiGenerated;
                }
                else
                {
                    var newTranslation = new Models.Entities.Translation
                    {
                        TranslationStringId = str.Id,
                        LanguageCode = targetLanguage,
                        TranslatedText = translatedText,
                        Source = TranslationSource.AiGenerated,
                        AiModel = "Translation AI"
                    };
                    _db.Translations.Add(newTranslation);
                }

                await _db.SaveChangesAsync(ct);
                translated++;

                // Invalidate cache
                _cache.Remove($"trans:{targetLanguage}:{str.Key}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to translate string {Key} to {Language}", str.Key, targetLanguage);
            }

            completed++;
        }

        progress?.Report(new TranslationProgress(total, completed, null));
        return translated;
    }

    public async Task<List<string>> GetAvailableLanguagesAsync(CancellationToken ct = default)
    {
        var languages = await _db.Translations
            .Select(t => t.LanguageCode)
            .Distinct()
            .OrderBy(l => l)
            .ToListAsync(ct);

        // Always include English as it's the default
        if (!languages.Contains("en"))
            languages.Insert(0, "en");

        return languages;
    }

    public async Task<TranslationStats> GetStatsAsync(string languageCode, CancellationToken ct = default)
    {
        var totalStrings = await _db.TranslationStrings.CountAsync(ct);

        if (languageCode == "en")
        {
            return new TranslationStats(
                LanguageCode: languageCode,
                TotalStrings: totalStrings,
                TranslatedStrings: totalStrings,
                PendingStrings: 0,
                CompletionPercentage: 100.0
            );
        }

        var translatedCount = await _db.Translations
            .Where(t => t.LanguageCode == languageCode)
            .CountAsync(ct);

        var pendingCount = totalStrings - translatedCount;
        var percentage = totalStrings > 0 ? (translatedCount / (double)totalStrings) * 100.0 : 0.0;

        return new TranslationStats(
            LanguageCode: languageCode,
            TotalStrings: totalStrings,
            TranslatedStrings: translatedCount,
            PendingStrings: pendingCount,
            CompletionPercentage: percentage
        );
    }

    public async Task<List<TranslationStringDto>> GetAllStringsWithTranslationsAsync(string languageCode, CancellationToken ct = default)
    {
        var strings = await _db.TranslationStrings
            .Include(ts => ts.Translations.Where(t => t.LanguageCode == languageCode))
            .ToListAsync(ct);

        return strings.Select(s => new TranslationStringDto(
            Key: s.Key,
            DefaultText: s.DefaultText,
            TranslatedText: s.Translations.FirstOrDefault()?.TranslatedText
        )).ToList();
    }
}
