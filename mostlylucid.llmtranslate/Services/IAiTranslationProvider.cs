namespace mostlylucid.llmtranslate.Services;

/// <summary>
/// Interface for AI-powered translation services
/// Consumers must implement this interface to provide translation capabilities
/// </summary>
public interface IAiTranslationProvider
{
    /// <summary>
    /// Translate text from one language to another
    /// </summary>
    /// <param name="text">Text to translate</param>
    /// <param name="targetLanguage">Target language code (ISO 639-1)</param>
    /// <param name="sourceLanguage">Source language code (ISO 639-1). Use "auto" for auto-detection</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Translated text</returns>
    Task<string> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = "en", CancellationToken ct = default);

    /// <summary>
    /// Batch translate multiple strings at once for better performance
    /// </summary>
    /// <param name="items">Dictionary of key-value pairs to translate</param>
    /// <param name="targetLanguage">Target language code</param>
    /// <param name="sourceLanguage">Source language code</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Dictionary of translated key-value pairs</returns>
    Task<Dictionary<string, string>> TranslateBatchAsync(
        Dictionary<string, string> items,
        string targetLanguage,
        string? sourceLanguage = "en",
        CancellationToken ct = default);
}
