using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Text;
using mostlylucid.llmtranslate.Services;

namespace mostlylucid.llmtranslate.TagHelpers;

/// <summary>
/// Tag helper for rendering a language selector dropdown
/// Usage: <language-selector style="dropdown" />
/// </summary>
[HtmlTargetElement("language-selector")]
public class LanguageSelectorTagHelper : TagHelper
{
    private readonly ITranslationService _translationService;

    /// <summary>
    /// Style of the language selector
    /// Options: "dropdown", "flags", "buttons", "select"
    /// Default: "dropdown"
    /// </summary>
    [HtmlAttributeName("style")]
    public string Style { get; set; } = "dropdown";

    /// <summary>
    /// CSS classes to add to the container
    /// </summary>
    [HtmlAttributeName("class")]
    public string? CssClass { get; set; }

    /// <summary>
    /// Whether to show language names (default: true)
    /// </summary>
    [HtmlAttributeName("show-names")]
    public bool ShowNames { get; set; } = true;

    /// <summary>
    /// Whether to show language codes (default: false)
    /// </summary>
    [HtmlAttributeName("show-codes")]
    public bool ShowCodes { get; set; } = false;

    /// <summary>
    /// Languages to show (comma-separated). If empty, shows all available languages.
    /// Example: "en,es,fr,de,ja"
    /// </summary>
    [HtmlAttributeName("languages")]
    public string? Languages { get; set; }

    public LanguageSelectorTagHelper(ITranslationService translationService)
    {
        _translationService = translationService;
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // Get available languages
        var availableLanguages = await _translationService.GetAvailableLanguagesAsync();

        // Filter languages if specified
        List<string> languagesToShow;
        if (!string.IsNullOrWhiteSpace(Languages))
        {
            var specifiedLangs = Languages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            languagesToShow = availableLanguages.Where(l => specifiedLangs.Contains(l, StringComparer.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            languagesToShow = availableLanguages;
        }

        // Render based on style
        var html = Style.ToLowerInvariant() switch
        {
            "dropdown" => RenderDropdown(languagesToShow),
            "flags" => RenderFlags(languagesToShow),
            "buttons" => RenderButtons(languagesToShow),
            "select" => RenderSelect(languagesToShow),
            _ => RenderDropdown(languagesToShow)
        };

        output.TagName = "div";
        output.Attributes.SetAttribute("class", $"language-selector {CssClass ?? ""}".Trim());
        output.Content.SetHtmlContent(html);
    }

    private string RenderDropdown(List<string> languages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"dropdown\">");
        sb.AppendLine("  <button class=\"btn dropdown-toggle\" type=\"button\" data-bs-toggle=\"dropdown\">");
        sb.AppendLine("    <span id=\"current-lang\">EN</span>");
        sb.AppendLine("  </button>");
        sb.AppendLine("  <ul class=\"dropdown-menu\">");

        foreach (var lang in languages)
        {
            var displayName = GetLanguageName(lang);
            var display = ShowNames && ShowCodes ? $"{displayName} ({lang.ToUpperInvariant()})" :
                         ShowNames ? displayName :
                         lang.ToUpperInvariant();

            sb.AppendLine($"    <li><a class=\"dropdown-item\" href=\"#\" onclick=\"setLanguage('{lang}'); return false;\">{display}</a></li>");
        }

        sb.AppendLine("  </ul>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private string RenderFlags(List<string> languages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"language-flags\">");

        foreach (var lang in languages)
        {
            var displayName = GetLanguageName(lang);
            sb.AppendLine($"  <button class=\"lang-flag-btn\" onclick=\"setLanguage('{lang}')\" title=\"{displayName}\">");
            sb.AppendLine($"    <span class=\"flag-icon flag-icon-{GetCountryCode(lang)}\"></span>");
            if (ShowNames)
            {
                sb.AppendLine($"    <span class=\"lang-name\">{(ShowCodes ? $"{displayName} ({lang.ToUpperInvariant()})" : displayName)}</span>");
            }
            sb.AppendLine("  </button>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine("<style>.language-flags { display: flex; gap: 0.5rem; } .lang-flag-btn { border: 1px solid #ddd; background: white; padding: 0.5rem; cursor: pointer; border-radius: 0.25rem; } .lang-flag-btn:hover { background: #f5f5f5; }</style>");
        return sb.ToString();
    }

    private string RenderButtons(List<string> languages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"language-buttons\">");

        foreach (var lang in languages)
        {
            var displayName = GetLanguageName(lang);
            var display = ShowNames && ShowCodes ? $"{displayName} ({lang.ToUpperInvariant()})" :
                         ShowNames ? displayName :
                         lang.ToUpperInvariant();

            sb.AppendLine($"  <button class=\"btn btn-outline-primary\" onclick=\"setLanguage('{lang}')\">{display}</button>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine("<style>.language-buttons { display: flex; gap: 0.5rem; flex-wrap: wrap; }</style>");
        return sb.ToString();
    }

    private string RenderSelect(List<string> languages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<select class=\"form-select\" onchange=\"setLanguage(this.value)\">");

        foreach (var lang in languages)
        {
            var displayName = GetLanguageName(lang);
            var display = ShowNames && ShowCodes ? $"{displayName} ({lang.ToUpperInvariant()})" :
                         ShowNames ? displayName :
                         lang.ToUpperInvariant();

            sb.AppendLine($"  <option value=\"{lang}\">{display}</option>");
        }

        sb.AppendLine("</select>");
        return sb.ToString();
    }

    private static string GetLanguageName(string code)
    {
        return code.ToLowerInvariant() switch
        {
            "en" => "English",
            "es" => "Español",
            "fr" => "Français",
            "de" => "Deutsch",
            "it" => "Italiano",
            "pt" => "Português",
            "ru" => "Русский",
            "ja" => "日本語",
            "ko" => "한국어",
            "zh" => "中文",
            "ar" => "العربية",
            "hi" => "हिन्दी",
            "nl" => "Nederlands",
            "pl" => "Polski",
            "tr" => "Türkçe",
            "sv" => "Svenska",
            "da" => "Dansk",
            "fi" => "Suomi",
            "no" => "Norsk",
            "cs" => "Čeština",
            "hu" => "Magyar",
            "ro" => "Română",
            "th" => "ไทย",
            "vi" => "Tiếng Việt",
            "id" => "Bahasa Indonesia",
            "ms" => "Bahasa Melayu",
            "uk" => "Українська",
            "el" => "Ελληνικά",
            "he" => "עברית",
            "fa" => "فارسی",
            _ => code.ToUpperInvariant()
        };
    }

    private static string GetCountryCode(string langCode)
    {
        // Map language codes to flag country codes
        return langCode.ToLowerInvariant() switch
        {
            "en" => "gb",
            "ja" => "jp",
            "ko" => "kr",
            "zh" => "cn",
            "ar" => "sa",
            "hi" => "in",
            "uk" => "ua",
            "cs" => "cz",
            "da" => "dk",
            "sv" => "se",
            "el" => "gr",
            "he" => "il",
            "fa" => "ir",
            _ => langCode
        };
    }
}
