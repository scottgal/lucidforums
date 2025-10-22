using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using mostlylucid.llmtranslate.Models;
using mostlylucid.llmtranslate.Storage;

namespace mostlylucid.llmtranslate.Services;

/// <summary>
/// Translation service implementation using JSON file storage
/// </summary>
public class JsonFileTranslationService : ITranslationService
{
    private readonly JsonFileTranslationStore _store;
    private readonly IAiTranslationProvider _ai;
    private readonly IMemoryCache _cache;
    private readonly RequestTranslationCache _requestCache;
    private readonly ILogger<JsonFileTranslationService> _logger;
    private readonly bool _enableMemoryCache;
    private readonly int _cacheDurationMinutes;

    public JsonFileTranslationService(
        JsonFileTranslationStore store,
        IAiTranslationProvider ai,
        IMemoryCache cache,
        RequestTranslationCache requestCache,
        ILogger<JsonFileTranslationService> logger,
        bool enableMemoryCache = true,
        int cacheDurationMinutes = 60)
    {
        _store = store;
        _ai = ai;
        _cache = cache;
        _requestCache = requestCache;
        _logger = logger;
        _enableMemoryCache = enableMemoryCache;
        _cacheDurationMinutes = cacheDurationMinutes;
    }

    public async Task<string> GetAsync(string key, string languageCode, CancellationToken ct = default)
    {
        // Try request-scoped cache first
        if (_requestCache.TryGet(languageCode, key, out var requestCached) && requestCached != null)
            return requestCached;

        // Try memory cache if enabled
        var cacheKey = $"trans:{languageCode}:{key}";
        if (_enableMemoryCache && _cache.TryGetValue<string>(cacheKey, out var cached))
        {
            _requestCache.Set(languageCode, key, cached!);
            return cached!;
        }

        // Get from file storage
        var translationString = await _store.GetStringAsync(key, ct);
        if (translationString == null)
        {
            _logger.LogWarning("Translation string not found for key: {Key}", key);
            return key;
        }

        // If requesting default language, return default text
        if (languageCode == "en")
        {
            if (_enableMemoryCache)
            {
                _cache.Set(cacheKey, translationString.DefaultText, TimeSpan.FromMinutes(_cacheDurationMinutes));
            }
            _requestCache.Set(languageCode, key, translationString.DefaultText);
            return translationString.DefaultText;
        }

        // Get translation for specific language
        var translation = await _store.GetTranslationAsync(key, languageCode, ct);
        var result = translation?.TranslatedText ?? translationString.DefaultText;

        if (_enableMemoryCache)
        {
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(_cacheDurationMinutes));
        }
        _requestCache.Set(languageCode, key, result);
        return result;
    }

    public async Task<int> EnsureStringAsync(string key, string defaultText, string? category = null, string? context = null, CancellationToken ct = default)
    {
        var existing = await _store.GetStringAsync(key, ct);
        if (existing != null)
        {
            if (existing.DefaultText != defaultText)
            {
                existing.DefaultText = defaultText;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                await _store.UpsertStringAsync(existing, ct);
            }
            return existing.Id;
        }

        var newString = new TranslationString
        {
            Key = key,
            DefaultText = defaultText,
            Category = category,
            Context = context,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        return await _store.UpsertStringAsync(newString, ct);
    }

    public async Task<int> TranslateAllStringsAsync(string targetLanguage, bool overwriteExisting = false, IProgress<TranslationProgress>? progress = null, CancellationToken ct = default)
    {
        var strings = await _store.GetAllStringsAsync(ct);
        var total = strings.Count;
        var completed = 0;
        var translated = 0;

        foreach (var str in strings)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(new TranslationProgress(total, completed, str.Key));

            var existing = await _store.GetTranslationAsync(str.Key, targetLanguage, ct);
            if (existing != null && !overwriteExisting)
            {
                completed++;
                continue;
            }

            try
            {
                var translatedText = await _ai.TranslateAsync(str.DefaultText, targetLanguage, "en", ct);

                var translation = new Translation
                {
                    LanguageCode = targetLanguage,
                    TranslatedText = translatedText,
                    Source = TranslationSource.AiGenerated,
                    AiModel = "Translation AI",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                await _store.UpsertTranslationAsync(str.Id, translation, ct);
                translated++;

                // Invalidate cache
                if (_enableMemoryCache)
                {
                    _cache.Remove($"trans:{targetLanguage}:{str.Key}");
                }
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
        return await _store.GetAvailableLanguagesAsync(ct);
    }

    public async Task<TranslationStats> GetStatsAsync(string languageCode, CancellationToken ct = default)
    {
        var allStrings = await _store.GetAllStringsAsync(ct);
        var totalStrings = allStrings.Count;

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

        var translations = await _store.GetAllTranslationsForLanguageAsync(languageCode, ct);
        var translatedCount = translations.Count;
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
        var strings = await _store.GetAllStringsAsync(ct);

        var result = new List<TranslationStringDto>();
        foreach (var str in strings)
        {
            var translation = await _store.GetTranslationAsync(str.Key, languageCode, ct);
            result.Add(new TranslationStringDto(
                Key: str.Key,
                DefaultText: str.DefaultText,
                TranslatedText: translation?.TranslatedText
            ));
        }

        return result;
    }
}
