using LucidForums.Data;
using LucidForums.Models.Entities;
using LucidForums.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using LucidForums.Helpers;
using System.Globalization;

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

    private static string NormalizeLanguageCode(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "en";
        var trimmed = input.Trim();

        // If contains region (e.g., en-US), take primary tag
        var primary = trimmed.Split('-')[0];

        // If already a 2-letter code and valid, return its two-letter code
        try
        {
            var culture = new CultureInfo(trimmed);
            return culture.TwoLetterISOLanguageName.ToLowerInvariant();
        }
        catch
        {
            // ignore
        }

        if (primary.Length <= 3)
        {
            try
            {
                var culture = new CultureInfo(primary);
                return culture.TwoLetterISOLanguageName.ToLowerInvariant();
            }
            catch
            {
                // ignore
            }
        }

        // Try match by English name prefix (e.g., "Spanish")
        var name = trimmed.ToLowerInvariant();
        try
        {
            var match = CultureInfo
                .GetCultures(CultureTypes.NeutralCultures | CultureTypes.SpecificCultures)
                .FirstOrDefault(c => c.EnglishName.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.TwoLetterISOLanguageName.ToLowerInvariant();
        }
        catch
        {
            // ignore
        }

        // Fallback to primary if looks like code, else English
        return (primary.Length == 2) ? primary.ToLowerInvariant() : "en";
    }

    public async Task<string> GetAsync(string key, string languageCode, CancellationToken ct = default)
    {
        var lang = NormalizeLanguageCode(languageCode);
        // Try request-scoped cache first (fastest, avoids concurrent DB access)
        if (_requestCache.TryGet(lang, key, out var requestCached) && requestCached != null)
            return requestCached;

        // Try memory cache
        var cacheKey = $"trans:{lang}:{key}";
        if (_cache.TryGetValue<string>(cacheKey, out var cached))
        {
            _requestCache.Set(lang, key, cached!);
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
            if (lang == "en")
            {
                _cache.Set(cacheKey, translationString.DefaultText, TimeSpan.FromHours(1));
                _requestCache.Set(lang, key, translationString.DefaultText);
                return translationString.DefaultText;
            }

            // Get the translation for this specific language
            var translation = await _db.Translations
                .AsNoTracking()
                .Where(t => t.TranslationString!.Key == key && t.LanguageCode == lang)
                .Select(t => t.TranslatedText)
                .FirstOrDefaultAsync(ct);

            var result = string.IsNullOrWhiteSpace(translation) ? translationString.DefaultText : translation;
            _cache.Set(cacheKey, result, TimeSpan.FromHours(1));
            _requestCache.Set(lang, key, result);
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
                        // Invalidate default language cache so UI picks up updated default immediately
                        _cache.Remove($"trans:en:{key}");
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
        // Normalize source language first (may detect via AI once) so cache keys are stable
        var src = sourceLanguage;
        if (string.IsNullOrWhiteSpace(src) || src.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var detector = new Charter
            {
                Name = "LanguageDetector",
                Purpose = "Detect the human language of the provided text and respond with only the English name of the language, e.g., 'English', 'Spanish', 'French'."
            };
            var detectPrompt = $"Return ONLY the language name in English, with no punctuation or extra words.\n\nText:\n{text}";

            // Cache language detection as well to avoid repeated calls for the same text
            var detectCacheKey = $"ai-detect:{ContentHash.Generate(text, 8)}";
            var detected = await _cache.GetOrCreateAsync(detectCacheKey, async entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromDays(7);
                var result = await _ai.GenerateAsync(detector, detectPrompt, ct: ct);
                return (result ?? string.Empty).Trim();
            });

            src = string.IsNullOrEmpty(detected) ? "English" : detected;
        }

        // Build a stable cache key for the translation request
        var normSrc = src?.Trim();
        var normTgt = targetLanguage?.Trim();
        var hash = ContentHash.Generate(text, 8);
        var cacheKey = $"ai-trans:{normSrc}:{normTgt}:{hash}";

        // Use memory cache to avoid repeated LLM calls; GetOrCreateAsync deduplicates concurrent callers
        var translated = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromDays(7);

            // Create a charter for translation context
            var charter = new Charter
            {
                Name = "Translation",
                Purpose = $"Translate text from {normSrc} to {normTgt} accurately and naturally"
            };

            var prompt = $@"Translate the following text from {normSrc} to {normTgt}.
Preserve formatting, placeholders (like {{0}}, {{name}}), and HTML tags if present.
Return ONLY the translated text, no explanations.

Text to translate:
{text}";

            var result = await _ai.GenerateAsync(charter, prompt, ct: ct);
            var cleaned = (result ?? string.Empty).Trim();
            // If AI returns empty, fall back to original text but do not cache empty responses
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                // Cache the fallback to prevent re-hitting the AI for known-problem inputs
                cleaned = text;
            }
            return cleaned;
        });

        return translated ?? text;
    }

    public async Task<int> TranslateAllStringsAsync(string targetLanguage, bool overwriteExisting = false, IProgress<TranslationProgress>? progress = null, CancellationToken ct = default)
    {
        var lang = NormalizeLanguageCode(targetLanguage);
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

            // Skip conditions refined: only skip if existing is up-to-date or manually approved
            var existing = str.Translations.FirstOrDefault(t => t.LanguageCode == lang);
            if (existing != null && !overwriteExisting)
            {
                // If manual or approved, never overwrite automatically
                if (existing.Source == TranslationSource.Manual || existing.IsApproved)
                {
                    completed++;
                    continue;
                }
                // If the existing translation is newer than or same as the default text, skip
                if (existing.UpdatedAtUtc >= str.UpdatedAtUtc)
                {
                    completed++;
                    continue;
                }
                // Otherwise, the default text changed after this translation; re-translate below
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
                        LanguageCode = lang,
                        TranslatedText = translatedText,
                        Source = TranslationSource.AiGenerated,
                        AiModel = "Translation AI"
                    };
                    _db.Translations.Add(newTranslation);
                }

                await _db.SaveChangesAsync(ct);
                translated++;

                // Invalidate cache
                _cache.Remove($"trans:{lang}:{str.Key}");
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
            .ToListAsync(ct);

        // Normalize and distinct
        languages = languages
            .Select(NormalizeLanguageCode)
            .Distinct()
            .OrderBy(l => l)
            .ToList();

        // Always include English as it's the default
        if (!languages.Contains("en"))
            languages.Insert(0, "en");

        return languages;
    }

    public async Task<TranslationStats> GetStatsAsync(string languageCode, CancellationToken ct = default)
    {
        var lang = NormalizeLanguageCode(languageCode);
        var totalStrings = await _db.TranslationStrings.CountAsync(ct);

        if (lang == "en")
        {
            return new TranslationStats(
                LanguageCode: lang,
                TotalStrings: totalStrings,
                TranslatedStrings: totalStrings,
                PendingStrings: 0,
                CompletionPercentage: 100.0
            );
        }

        var translatedCount = await _db.Translations
            .Where(t => t.LanguageCode == lang)
            .CountAsync(ct);

        var pendingCount = totalStrings - translatedCount;
        var percentage = totalStrings > 0 ? (translatedCount / (double)totalStrings) * 100.0 : 0.0;

        return new TranslationStats(
            LanguageCode: lang,
            TotalStrings: totalStrings,
            TranslatedStrings: translatedCount,
            PendingStrings: pendingCount,
            CompletionPercentage: percentage
        );
    }

    public async Task<List<TranslationStringDto>> GetAllStringsWithTranslationsAsync(string languageCode, CancellationToken ct = default)
    {
        var lang = NormalizeLanguageCode(languageCode);
        var strings = await _db.TranslationStrings
            .Include(ts => ts.Translations.Where(t => t.LanguageCode == lang))
            .ToListAsync(ct);

        return strings.Select(s => new TranslationStringDto(
            Key: s.Key,
            DefaultText: s.DefaultText,
            TranslatedText: s.Translations.FirstOrDefault()?.TranslatedText
        )).ToList();
    }
}
