namespace LucidForums.Services.Translation;

/// <summary>
/// Service for managing UI string translations
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// Get translated text for a key in the specified language
    /// Falls back to default text if translation not found
    /// </summary>
    Task<string> GetAsync(string key, string languageCode, CancellationToken ct = default);

    /// <summary>
    /// Get or create a translation string by key
    /// </summary>
    Task<int> EnsureStringAsync(string key, string defaultText, string? category = null, string? context = null, CancellationToken ct = default);

    /// <summary>
    /// Translate a string using AI
    /// </summary>
    Task<string> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = "en", CancellationToken ct = default);

    /// <summary>
    /// Bulk translate all strings for a language using AI
    /// </summary>
    Task<int> TranslateAllStringsAsync(string targetLanguage, bool overwriteExisting = false, IProgress<TranslationProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Get all available language codes that have translations
    /// </summary>
    Task<List<string>> GetAvailableLanguagesAsync(CancellationToken ct = default);

    /// <summary>
    /// Get translation statistics for a language
    /// </summary>
    Task<TranslationStats> GetStatsAsync(string languageCode, CancellationToken ct = default);

    /// <summary>
    /// Get all translation strings with their translations for a language
    /// </summary>
    Task<List<TranslationStringDto>> GetAllStringsWithTranslationsAsync(string languageCode, CancellationToken ct = default);
}

public record TranslationStringDto(string Key, string DefaultText, string? TranslatedText);

public record TranslationProgress(int Total, int Completed, string? CurrentKey);

public record TranslationStats(
    string LanguageCode,
    int TotalStrings,
    int TranslatedStrings,
    int PendingStrings,
    double CompletionPercentage
);
