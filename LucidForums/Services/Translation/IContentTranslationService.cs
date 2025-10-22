namespace LucidForums.Services.Translation;

/// <summary>
/// Service for translating user-generated content (forums, threads, messages)
/// </summary>
public interface IContentTranslationService
{
    /// <summary>
    /// Get translated content for a specific field
    /// </summary>
    Task<string?> GetTranslationAsync(string contentType, string contentId, string fieldName, string languageCode, CancellationToken ct = default);

    /// <summary>
    /// Translate a specific piece of content
    /// </summary>
    Task<string> TranslateContentAsync(string contentType, string contentId, string fieldName, string sourceText, string targetLanguage, CancellationToken ct = default);

    /// <summary>
    /// Batch translate all content of a type for a language
    /// </summary>
    Task<int> TranslateAllContentAsync(string contentType, string targetLanguage, IProgress<TranslationProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Mark translations as stale when source content changes
    /// </summary>
    Task MarkStaleAsync(string contentType, string contentId, string fieldName, CancellationToken ct = default);
}
