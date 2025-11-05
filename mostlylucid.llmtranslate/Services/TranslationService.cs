using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using mostlylucid.llmtranslate.Models;

namespace mostlylucid.llmtranslate.Services;

/// <summary>
/// Minimal placeholder TranslationService implementation for the package to compile.
/// For JSON-file storage scenarios, use JsonFileTranslationService (registered by AddAutoTranslate with JsonFile).
/// This implementation returns sensible defaults and does not persist to a database.
/// </summary>
public class TranslationService : ITranslationService
{
    private readonly ILogger<TranslationService> _logger;

    public TranslationService(ILogger<TranslationService> logger)
    {
        _logger = logger;
    }

    public Task<string> GetAsync(string key, string languageCode, CancellationToken ct = default)
    {
        // Placeholder: return key for non-English, which is better than throwing during startup
        if (string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(key);
        return Task.FromResult(key);
    }

    public Task<int> EnsureStringAsync(string key, string defaultText, string? category = null, string? context = null, CancellationToken ct = default)
    {
        // No persistence in this minimal implementation
        _logger.LogDebug("EnsureString called for {Key}", key);
        return Task.FromResult(0);
    }

    public Task<int> TranslateAllStringsAsync(string targetLanguage, bool overwriteExisting = false, IProgress<TranslationProgress>? progress = null, CancellationToken ct = default)
    {
        _logger.LogInformation("TranslateAllStringsAsync placeholder called for {Lang}", targetLanguage);
        return Task.FromResult(0);
    }

    public Task<List<string>> GetAvailableLanguagesAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new List<string> { "en" });
    }

    public Task<TranslationStats> GetStatsAsync(string languageCode, CancellationToken ct = default)
    {
        var stats = new TranslationStats(languageCode, 0, 0, 0, 0);
        return Task.FromResult(stats);
    }

    public Task<List<TranslationStringDto>> GetAllStringsWithTranslationsAsync(string languageCode, CancellationToken ct = default)
    {
        return Task.FromResult(new List<TranslationStringDto>());
    }
}