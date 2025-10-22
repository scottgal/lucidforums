using Microsoft.AspNetCore.Mvc;
using mostlylucid.llmtranslate.Services;

namespace mostlylucid.llmtranslate.Controllers;

/// <summary>
/// Controller for language switching and translation management
/// </summary>
[Route("[controller]")]
public class LanguageController : Controller
{
    private readonly ITranslationService _translationService;
    private readonly IPageLanguageSwitchService _switchService;

    public LanguageController(ITranslationService translationService, IPageLanguageSwitchService switchService)
    {
        _translationService = translationService;
        _switchService = switchService;
    }

    /// <summary>
    /// Set language preference cookie
    /// </summary>
    [HttpPost("Set")]
    public IActionResult Set([FromForm] string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return BadRequest();

        Response.Cookies.Append("preferred-language", languageCode, new Microsoft.AspNetCore.Http.CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            HttpOnly = false // JavaScript needs to read this
        });

        return Ok();
    }

    /// <summary>
    /// Get list of available language codes
    /// </summary>
    [HttpGet("Available")]
    public async Task<IActionResult> Available()
    {
        var languages = await _translationService.GetAvailableLanguagesAsync();
        return Ok(languages);
    }

    /// <summary>
    /// Get all translations for a language as JSON
    /// </summary>
    [HttpGet("GetAll/{languageCode}")]
    public async Task<IActionResult> GetAll(string languageCode)
    {
        var strings = await _translationService.GetAllStringsWithTranslationsAsync(languageCode);
        return Ok(strings);
    }

    /// <summary>
    /// HTMX endpoint for language switching - returns OOB swaps
    /// </summary>
    [HttpPost("Switch/{languageCode}")]
    public async Task<IActionResult> Switch(string languageCode, [FromForm] List<string> keys)
    {
        // Set cookie for future requests
        Response.Cookies.Append("preferred-language", languageCode, new Microsoft.AspNetCore.Http.CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            HttpOnly = false
        });

        // Build OOB swap response
        var html = await _switchService.BuildSwitchResponseAsync(languageCode, keys ?? new List<string>());

        return Content(html, "text/html");
    }
}
