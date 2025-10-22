using System.Security.Cryptography;
using System.Text;
using LucidForums.Services.Translation;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

[Route("Language")]
public class LanguageController : Controller
{
    private readonly ITranslationService _translationService;

    public LanguageController(ITranslationService translationService)
    {
        _translationService = translationService;
    }

    [HttpPost("Set")]
    public async Task<IActionResult> SetLanguage([FromForm] string languageCode, [FromForm] string? returnUrl)
    {
        // Set cookie for language preference
        Response.Cookies.Append("preferred-language", languageCode, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = false, // Allow JavaScript to read it
            SameSite = SameSiteMode.Lax
        });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpGet("Available")]
    public async Task<IActionResult> GetAvailableLanguages(CancellationToken ct)
    {
        var languages = await _translationService.GetAvailableLanguagesAsync(ct);
        return Json(languages);
    }

    [HttpGet("GetAll/{languageCode}")]
    public async Task<IActionResult> GetAllTranslations(string languageCode, CancellationToken ct)
    {
        // Get all translation strings
        var strings = await _translationService.GetAllStringsWithTranslationsAsync(languageCode, ct);

        // Return as dictionary for easy JavaScript access
        var translations = strings.ToDictionary(
            s => s.Key,
            s => s.TranslatedText ?? s.DefaultText
        );

        return Json(translations);
    }

    /// <summary>
    /// HTMX endpoint that returns OOB swaps for all translatable elements on the page
    /// This is more efficient than JSON + JavaScript as HTMX handles the DOM updates
    /// </summary>
    [HttpPost("Switch/{languageCode}")]
    public async Task<IActionResult> SwitchLanguage(string languageCode, [FromForm] string[] keys, CancellationToken ct)
    {
        // Set the language cookie
        Response.Cookies.Append("preferred-language", languageCode, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = false,
            SameSite = SameSiteMode.Lax
        });

        // Get translations for the requested keys
        var html = new StringBuilder();

        foreach (var key in keys)
        {
            var translatedText = await _translationService.GetAsync(key, languageCode, ct);
            var elementId = $"t-{GenerateContentHash(key)}";

            // Generate HTMX OOB swap element
            html.AppendLine($"<span id=\"{elementId}\" hx-swap-oob=\"innerHTML\">{System.Net.WebUtility.HtmlEncode(translatedText)}</span>");
        }

        // Also update the language indicator
        html.AppendLine($"<span id=\"current-lang\" hx-swap-oob=\"innerHTML\">{languageCode.ToUpperInvariant()}</span>");

        return Content(html.ToString(), "text/html");
    }

    private static string GenerateContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }
}
