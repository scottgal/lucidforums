using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using mostlylucid.llmtranslate.Helpers;

namespace mostlylucid.llmtranslate.TagHelpers;

/// <summary>
/// Automatically translates any HTML element with auto-translate="true"
/// Generates translation key from slugified content + content hash
/// </summary>
[HtmlTargetElement(Attributes = "auto-translate")]
public partial class AutoTranslateTagHelper : TagHelper
{
    private readonly TranslationHelper _translator;

    public AutoTranslateTagHelper(TranslationHelper translator)
    {
        _translator = translator;
    }

    [HtmlAttributeName("auto-translate")]
    public bool AutoTranslate { get; set; }

    [HtmlAttributeName("translation-category")]
    public string? Category { get; set; }

    public override int Order => -1000; // Run early

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NonAlphanumericRegex();

    private static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "text";

        // Remove HTML tags
        var withoutTags = Regex.Replace(text, @"<[^>]+>", "");

        // Convert to lowercase and replace non-alphanumeric with hyphens
        var slug = NonAlphanumericRegex().Replace(withoutTags.ToLowerInvariant(), "-");

        // Remove leading/trailing hyphens and collapse multiple hyphens
        slug = Regex.Replace(slug.Trim('-'), @"-+", "-");

        // Limit length and ensure it ends cleanly
        if (slug.Length > 50)
        {
            slug = slug.Substring(0, 50).TrimEnd('-');
        }

        return string.IsNullOrEmpty(slug) ? "text" : slug;
    }

    private static string GenerateTranslationKey(string content, string? category = null)
    {
        var slug = Slugify(content);
        var hash = ContentHash.Generate(content, 4);

        // Format: [category.]slug-hash
        if (!string.IsNullOrEmpty(category))
        {
            return $"{category}.{slug}-{hash}";
        }

        return $"{slug}-{hash}";
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (!AutoTranslate)
        {
            return;
        }

        // Get the inner content
        var content = await output.GetChildContentAsync();
        var originalText = content.GetContent();

        if (string.IsNullOrWhiteSpace(originalText))
        {
            return;
        }

        // Generate automatic translation key from content
        var translationKey = GenerateTranslationKey(originalText, Category);

        // Get or create translation
        var translatedText = await _translator.T(translationKey, originalText);

        // Generate deterministic ID for HTMX OOB targeting
        var elementId = $"t-{ContentHash.Generate(translationKey)}";

        // Add attributes for translation system
        output.Attributes.RemoveAll("auto-translate");
        output.Attributes.SetAttribute("id", elementId);
        output.Attributes.SetAttribute("data-translate-key", translationKey);
        output.Attributes.SetAttribute("data-content-hash", ContentHash.Generate(originalText));

        // Set translated content; allow HTML in translations
        output.Content.SetHtmlContent(translatedText);
    }
}
