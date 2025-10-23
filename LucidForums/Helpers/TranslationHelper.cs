using LucidForums.Services.Translation;
using Microsoft.AspNetCore.Http;

namespace LucidForums.Helpers;

/// <summary>
/// Helper class for accessing translations in views
/// </summary>
public class TranslationHelper
{
    private readonly ITranslationService _translationService;
    private readonly IContentTranslationService _contentTranslationService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TranslationHelper(ITranslationService translationService, IContentTranslationService contentTranslationService, IHttpContextAccessor httpContextAccessor)
    {
        _translationService = translationService;
        _contentTranslationService = contentTranslationService;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Get the current user's preferred language from cookie or default to English
    /// </summary>
    public string GetCurrentLanguage()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return "en";

        // Try to get language from cookie
        if (context.Request.Cookies.TryGetValue("preferred-language", out var lang))
            return lang;

        // Try to get from Accept-Language header
        var acceptLanguage = context.Request.Headers.AcceptLanguage.FirstOrDefault();
        if (!string.IsNullOrEmpty(acceptLanguage))
        {
            var primaryLang = acceptLanguage.Split(',').FirstOrDefault()?.Split(';').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(primaryLang))
            {
                // Extract just the language code (e.g., "en" from "en-US")
                var langCode = primaryLang.Split('-').FirstOrDefault();
                if (!string.IsNullOrEmpty(langCode))
                    return langCode.ToLowerInvariant();
            }
        }

        return "en";
    }

    /// <summary>
    /// Translate a string using the current user's language
    /// </summary>
    public async Task<string> T(string key, string? defaultText = null)
    {
        var language = GetCurrentLanguage();

        // If default text provided and key doesn't exist, ensure it's registered BEFORE fetching
        if (defaultText != null)
        {
            try
            {
                // Ensure the default string is present so first render never falls back to the key
                await _translationService.EnsureStringAsync(key, defaultText);
            }
            catch
            {
                // Best effort - don't block rendering
            }
        }

        return await _translationService.GetAsync(key, language);
    }

    /// <summary>
    /// Translate a string with a specific language
    /// </summary>
    public async Task<string> T(string key, string languageCode, string? defaultText = null)
    {
        if (defaultText != null)
        {
            try
            {
                await _translationService.EnsureStringAsync(key, defaultText);
            }
            catch
            {
                // Best effort
            }
        }

        return await _translationService.GetAsync(key, languageCode);
    }

    /// <summary>
    /// Get or trigger translation for content (Thread, Forum, Message)
    /// Returns the translated text if available, otherwise returns original and triggers translation
    /// </summary>
    public async Task<string> TranslateContentAsync(string contentType, string contentId, string fieldName, string sourceText, CancellationToken ct = default)
    {
        var language = GetCurrentLanguage();

        // If English, return source text immediately
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
            return sourceText;

        try
        {
            // Check if translation exists
            var existing = await _contentTranslationService.GetTranslationAsync(contentType, contentId, fieldName, language, ct);
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;

            // Trigger translation in background (SignalR will notify when complete)
            _ = _contentTranslationService.TranslateContentAsync(contentType, contentId, fieldName, sourceText, language, ct);

            // Return source text while translation is in progress
            return sourceText;
        }
        catch
        {
            // Best effort - return source text on any error
            return sourceText;
        }
    }

    /// <summary>
    /// Check if content needs translation for current language
    /// </summary>
    public bool NeedsTranslation()
    {
        var language = GetCurrentLanguage();
        return !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
    }
}
