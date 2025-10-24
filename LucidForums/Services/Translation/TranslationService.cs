using LucidForums.Data;
using LucidForums.Models.Entities;
using LucidForums.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using LucidForums.Helpers;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace LucidForums.Services.Translation;

public class TranslationService : ITranslationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ITextAiService _ai;
    private readonly IMemoryCache _cache;
    private readonly RequestTranslationCache _requestCache;
    private readonly ILogger<TranslationService> _logger;
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private object? _externalTranslator; // resolved lazily to avoid hard reference if package not present (NOT readonly so it can be cached)
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public TranslationService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ITextAiService ai,
        IMemoryCache cache,
        RequestTranslationCache requestCache,
        ILogger<TranslationService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _dbFactory = dbFactory;
        _ai = ai;
        _cache = cache;
        _requestCache = requestCache;
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
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
            // Use a single query with Include to avoid multiple DbContext operations
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var translationData = await db.TranslationStrings
                .AsNoTracking()
                .Where(ts => ts.Key == key)
                .Select(ts => new
                {
                    ts.DefaultText,
                    Translation = ts.Translations
                        .Where(t => t.LanguageCode == lang)
                        .Select(t => t.TranslatedText)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync(ct);

            if (translationData == null)
            {
                _logger.LogWarning("Translation string not found for key: {Key}", key);
                return key; // Return key as fallback
            }

            // If requesting default language, return default text
            if (lang == "en")
            {
                _cache.Set(cacheKey, translationData.DefaultText, TimeSpan.FromHours(1));
                _requestCache.Set(lang, key, translationData.DefaultText);
                return translationData.DefaultText;
            }

            // Use translation if available, otherwise fall back to default text
            var result = string.IsNullOrWhiteSpace(translationData.Translation)
                ? translationData.DefaultText
                : translationData.Translation;
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
            // Use a single tracked query to avoid mixing tracked/untracked
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var existing = await db.TranslationStrings
                .Where(ts => ts.Key == key)
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                // Update if default text changed
                if (existing.DefaultText != defaultText)
                {
                    existing.DefaultText = defaultText;
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    // Invalidate default language cache so UI picks up updated default immediately
                    _cache.Remove($"trans:en:{key}");
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

            db.TranslationStrings.Add(newString);
            await db.SaveChangesAsync(ct);
            return newString.Id;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private static string CleanAiOutput(string aiOutput, string originalText)
    {
        if (string.IsNullOrWhiteSpace(aiOutput)) return originalText;
        var s = aiOutput.Trim();

        // Strip Markdown code fences
        if (s.StartsWith("```"))
        {
            var end = s.IndexOf("```", 3, StringComparison.Ordinal);
            if (end > 3)
            {
                s = s.Substring(3, end - 3).Trim();
            }
        }

        // Strip surrounding quotes
        if ((s.StartsWith("\"") && s.EndsWith("\"")) || (s.StartsWith("“") && s.EndsWith("”")) || (s.StartsWith("'") && s.EndsWith("'")))
        {
            s = s.Substring(1, s.Length - 2).Trim();
        }

        // Remove leading language/code labels like "es:" or "s :"
        s = System.Text.RegularExpressions.Regex.Replace(s, @"^\s*[a-z]{1,3}\s*:\s*", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove trailing or standalone notes like "(Note: ...)" at the end
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*\(Note:[\s\S]*\)$", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove any lines that begin with Note-like prefixes
        var lines = s.Split('\n');
        lines = lines.Where(l => !System.Text.RegularExpressions.Regex.IsMatch(l.Trim(), @"^(note|nota|remarque|hinweis)\s*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToArray();
        s = string.Join("\n", lines).Trim();

        return string.IsNullOrWhiteSpace(s) ? originalText : s;
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

        // Use memory cache to avoid repeated calls
        var translated = await _cache.GetOrCreateAsync<string>(cacheKey, async entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromDays(7);

            // Decide order based on configured provider. Default to EasyNMT-first.
            var providerMode = (_configuration["Translation:Provider"] ?? "easynmt").Trim().ToLowerInvariant();
            var srcCodeEff = NormalizeLanguageCode(normSrc);
            var tgtCodeEff = NormalizeLanguageCode(normTgt);

            async Task<string?> TryExternalAsync()
            {
                try
                {
                    var isEnabled = IsExternalTranslationEnabled();
                    _logger.LogDebug("External translation enabled: {Enabled}", isEnabled);
                    if (!isEnabled) return null;

                    var ext = GetExternalTranslator();
                    _logger.LogDebug("External translator resolved: {HasTranslator}", ext != null);
                    if (ext == null) return null;

                    _logger.LogInformation("Calling EasyNMT for translation: {Text} -> {Target}", text.Substring(0, Math.Min(50, text.Length)), tgtCodeEff);
                    var t = await InvokeExternalTranslateAsync(ext, text, tgtCodeEff,
                        srcCodeEff == "en" && string.Equals(src, "English", StringComparison.OrdinalIgnoreCase) ? "en" : srcCodeEff, ct);

                    if (string.IsNullOrWhiteSpace(t))
                    {
                        _logger.LogWarning("EasyNMT returned empty translation");
                        return null;
                    }

                    // Treat exact echo as failure (common on server errors)
                    if (string.Equals(t, text, StringComparison.Ordinal))
                    {
                        _logger.LogWarning("EasyNMT returned original text (likely error)");
                        return null;
                    }

                    _logger.LogInformation("EasyNMT translation successful: {Result}", t.Substring(0, Math.Min(50, t.Length)));
                    return t;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "External translator failed");
                    return null;
                }
            }

            async Task<string?> TryLlmAsync()
            {
                try
                {
                    var charter = new Charter
                    {
                        Name = "Translation",
                        Purpose = $"Translate text from {normSrc} to {normTgt} accurately and naturally"
                    };

                    var prompt = $@"Translate the following text from {normSrc} to {normTgt}.
Preserve formatting, placeholders (like {{0}}, {{name}}), and HTML tags if present.
Return ONLY the translated text. Do not add any notes, explanations, language labels (like 'es:'), quotes, or extra punctuation. Output should be the translation text only.

Text to translate:
{text}";

                    var result = await _ai.GenerateAsync(charter, prompt, ct: ct);
                    var cleaned = CleanAiOutput(result ?? string.Empty, text);
                    if (string.IsNullOrWhiteSpace(cleaned)) return null;
                    return cleaned;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LLM translation failed");
                    return null;
                }
            }

            if (providerMode == "llm")
            {
                // LLM first, then fallback to EasyNMT if available
                var llm = await TryLlmAsync();
                if (!string.IsNullOrWhiteSpace(llm)) return llm!;
                var ext = await TryExternalAsync();
                if (!string.IsNullOrWhiteSpace(ext)) return ext!;
                return text; // final fallback
            }
            else
            {
                // EasyNMT first (default), then fallback to LLM
                var ext = await TryExternalAsync();
                if (!string.IsNullOrWhiteSpace(ext)) return ext!;

                _logger.LogInformation("EasyNMT failed or unavailable, falling back to LLM translation for {Source} -> {Target}", normSrc, normTgt);
                var llm = await TryLlmAsync();
                if (!string.IsNullOrWhiteSpace(llm))
                {
                    _logger.LogInformation("LLM translation successful as fallback");
                    return llm!;
                }

                _logger.LogWarning("Both EasyNMT and LLM translation failed, returning original text");
                return text; // final fallback
            }
        });

        return translated ?? text;
    }

    private bool IsExternalTranslationEnabled()
    {
        try
        {
            var provider = _configuration["Translation:Provider"]; // e.g., "easynmt"
            if (!string.IsNullOrWhiteSpace(provider) && provider.Equals("easynmt", StringComparison.OrdinalIgnoreCase))
                return true;

            // Consider both environment-style and appsettings-style endpoints
            var endpointEnv = _configuration["EASYNMT_ENDPOINT"]; // e.g., http://host.docker.internal:24080/
            if (!string.IsNullOrWhiteSpace(endpointEnv)) return true;

            var endpointConfig = _configuration["Translation:EasyNmt:Endpoint"]; // e.g., http://localhost:24080/
            return !string.IsNullOrWhiteSpace(endpointConfig);
        }
        catch
        {
            return false;
        }
    }

    private object? GetExternalTranslator()
    {
        if (_externalTranslator != null) return _externalTranslator;
        try
        {
            var ifaceType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(a => a.GetType("mostlylucid.llmtranslate.Services.IAiTranslationProvider"))
                .FirstOrDefault(t => t != null);
            if (ifaceType == null)
            {
                _logger.LogDebug("Could not find IAiTranslationProvider type in loaded assemblies");
                return null;
            }
            var svc = _serviceProvider.GetService(ifaceType);
            if (svc != null)
            {
                _externalTranslator = svc; // Cache the resolved service
                _logger.LogDebug("Resolved and cached external translator: {Type}", svc.GetType().Name);
            }
            else
            {
                _logger.LogDebug("IAiTranslationProvider type found but service not registered in DI");
            }
            return svc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving external translator");
            return null;
        }
    }

    private async Task<string?> InvokeExternalTranslateAsync(object translator, string text, string targetLanguage, string? sourceLanguage, CancellationToken ct)
    {
        try
        {
            var type = translator.GetType();
            var method = type.GetMethod("TranslateAsync", new[] { typeof(string), typeof(string), typeof(string), typeof(CancellationToken) });
            if (method == null)
            {
                // Try overload with optional sourceLanguage (nullable)
                var methods = type.GetMethods().Where(m => m.Name == "TranslateAsync").ToList();
                method = methods.FirstOrDefault();
            }
            if (method == null) return null;
            var args = new object?[] { text, targetLanguage, sourceLanguage, ct };
            var task = method.Invoke(translator, args) as Task<string>;
            if (task != null)
            {
                return await task.ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to call external translator via reflection");
        }
        return null;
    }

    public async Task<int> TranslateAllStringsAsync(string targetLanguage, bool overwriteExisting = false, IProgress<TranslationProgress>? progress = null, CancellationToken ct = default)
    {
        var lang = NormalizeLanguageCode(targetLanguage);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var strings = await db.TranslationStrings
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
                    db.Translations.Add(newTranslation);
                }

                await db.SaveChangesAsync(ct);
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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var languages = await db.Translations
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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var totalStrings = await db.TranslationStrings.CountAsync(ct);

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

        var translatedCount = await db.Translations
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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var strings = await db.TranslationStrings
            .Include(ts => ts.Translations.Where(t => t.LanguageCode == lang))
            .ToListAsync(ct);

        return strings.Select(s => new TranslationStringDto(
            Key: s.Key,
            DefaultText: s.DefaultText,
            TranslatedText: s.Translations.FirstOrDefault()?.TranslatedText
        )).ToList();
    }

    public async Task<string> DetectLanguageAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "en"; // Default to English for empty text
        }

        // Cache language detection to avoid repeated calls
        var detectCacheKey = $"lang-detect:{ContentHash.Generate(text, 8)}";
        var detected = await _cache.GetOrCreateAsync(detectCacheKey, async entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromDays(7);

            var detector = new Charter
            {
                Name = "LanguageDetector",
                Purpose = "Detect the human language of the provided text and respond with ONLY the two-letter ISO 639-1 language code (e.g., 'en', 'es', 'fr', 'de', 'ja', 'zh')."
            };

            var detectPrompt = $@"Detect the language of the following text and return ONLY the two-letter ISO 639-1 language code.
Do not include any punctuation, explanation, or extra text - just the two-letter code in lowercase.

Examples:
- For English text, return: en
- For Spanish text, return: es
- For French text, return: fr
- For German text, return: de
- For Japanese text, return: ja
- For Chinese text, return: zh

Text to detect:
{text}";

            var result = await _ai.GenerateAsync(detector, detectPrompt, ct: ct);
            var cleaned = (result ?? "en").Trim().ToLowerInvariant();

            // Extract just the language code if AI returned more than expected
            if (cleaned.Length > 2)
            {
                // Try to extract a 2-letter code
                var match = System.Text.RegularExpressions.Regex.Match(cleaned, @"\b([a-z]{2})\b");
                if (match.Success)
                {
                    cleaned = match.Groups[1].Value;
                }
                else
                {
                    cleaned = cleaned.Substring(0, Math.Min(2, cleaned.Length));
                }
            }

            // Validate it's a valid 2-letter code
            if (cleaned.Length != 2 || !cleaned.All(char.IsLetter))
            {
                _logger.LogWarning("Language detection returned invalid code '{Code}', defaulting to 'en'", cleaned);
                return "en";
            }

            return cleaned;
        });

        return detected ?? "en";
    }
}
