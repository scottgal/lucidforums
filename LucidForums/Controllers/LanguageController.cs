using System.Diagnostics;
using System.Text;
using LucidForums.Helpers;
using LucidForums.Services.Translation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LucidForums.Data;
using Microsoft.AspNetCore.SignalR;
using LucidForums.Hubs;
using LucidForums.Services.Ai;
using LucidForums.Models.Entities;

namespace LucidForums.Controllers;

[Route("Language")]
public class LanguageController : Controller
{
    private readonly ITranslationService _translationService;
    private readonly IPageLanguageSwitchService _pageSwitchService;

    public LanguageController(ITranslationService translationService, IPageLanguageSwitchService pageSwitchService)
    {
        _translationService = translationService;
        _pageSwitchService = pageSwitchService;
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

        var html = await _pageSwitchService.BuildSwitchResponseAsync(languageCode, keys, ct);
        return Content(html, "text/html");
    }

}
