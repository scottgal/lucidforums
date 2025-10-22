using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using LucidForums.Helpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace LucidForums.TagHelpers;

/// <summary>
/// Tag helper for translating text in views with HTMX OOB swap support
/// Usage: <t key="home.welcome">Welcome to LucidForums</t>
/// </summary>
[HtmlTargetElement("t")]
public class TranslateTagHelper : TagHelper
{
    private readonly TranslationHelper _translator;

    [HtmlAttributeName("key")]
    public string? Key { get; set; }

    [HtmlAttributeName("lang")]
    public string? Language { get; set; }

    [HtmlAttributeName("category")]
    public string? Category { get; set; }

    public TranslateTagHelper(TranslationHelper translator)
    {
        _translator = translator;
    }

    /// <summary>
    /// Generate a compact hash of the content for HTMX OOB targeting
    /// </summary>
    private static string GenerateContentHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = XxHash64.Hash(bytes);
        // Take first 8 bytes and convert to hex for a compact identifier
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // Get the default text from the tag content
        var content = await output.GetChildContentAsync();
        var defaultText = content.GetContent();

        if (string.IsNullOrEmpty(Key))
        {
            // If no key provided, output the default text as-is
            output.TagName = null; // Remove the <t> tag
            output.Content.SetContent(defaultText);
            return;
        }

        // Get translated text
        var translatedText = string.IsNullOrEmpty(Language)
            ? await _translator.T(Key, defaultText)
            : await _translator.T(Key, Language, defaultText);

        // Generate a unique, compact ID based on the translation key for HTMX targeting
        // Using a hash ensures IDs are valid and collision-resistant
        var elementId = $"t-{GenerateContentHash(Key)}";

        // Output span with attributes optimized for HTMX OOB swaps
        output.TagName = "span";
        output.Attributes.SetAttribute("id", elementId); // Required for HTMX OOB targeting
        output.Attributes.SetAttribute("data-translate-key", Key);
        output.Attributes.SetAttribute("data-content-hash", GenerateContentHash(defaultText)); // For change detection

        if (!string.IsNullOrEmpty(Category))
            output.Attributes.SetAttribute("data-translate-category", Category);

        output.Content.SetContent(translatedText);
    }
}
