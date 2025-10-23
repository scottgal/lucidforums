using System.Text.RegularExpressions;
using LucidForums.Helpers;
using LucidForums.Services.Translation;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace LucidForums.TagHelpers;

/// <summary>
/// Automatically translates any HTML element with auto-translate="true"
/// Supports both UI string translations and user-generated content translations (Forum, Thread, Message)
/// Generates translation key from slugified content + content hash for UI strings
/// For content translations, uses ContentTranslationService with content type, ID, and field name
/// </summary>
[HtmlTargetElement(Attributes = "auto-translate")]
public partial class AutoTranslateTagHelper : TagHelper
{
    private readonly TranslationHelper _translator;
    private readonly IContentTranslationService _contentTranslationService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoTranslateTagHelper> _logger;

    public AutoTranslateTagHelper(
        TranslationHelper translator,
        IContentTranslationService contentTranslationService,
        IServiceProvider serviceProvider,
        ILogger<AutoTranslateTagHelper> logger)
    {
        _translator = translator;
        _contentTranslationService = contentTranslationService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    [HtmlAttributeName("auto-translate")]
    public bool AutoTranslate { get; set; }

    [HtmlAttributeName("translation-category")]
    public string? Category { get; set; }

    [HtmlAttributeName("translation-field")]
    public string? TranslationField { get; set; }

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

        // Check if this is a content translation (Forum, Thread, Message)
        // by looking for data-*-id attributes in the output
        var forumId = output.Attributes.FirstOrDefault(a => a.Name == "data-forum-id")?.Value?.ToString();
        var threadId = output.Attributes.FirstOrDefault(a => a.Name == "data-thread-id")?.Value?.ToString();
        var messageId = output.Attributes.FirstOrDefault(a => a.Name == "data-message-id")?.Value?.ToString();

        var isContentTranslation = !string.IsNullOrWhiteSpace(TranslationField) &&
            (!string.IsNullOrWhiteSpace(forumId) || !string.IsNullOrWhiteSpace(threadId) || !string.IsNullOrWhiteSpace(messageId));

        if (isContentTranslation)
        {
            await ProcessContentTranslationAsync(originalText, output, forumId, threadId, messageId);
        }
        else
        {
            await ProcessUiStringTranslationAsync(originalText, output);
        }
    }

    private async Task ProcessUiStringTranslationAsync(string originalText, TagHelperOutput output)
    {
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

    private async Task ProcessContentTranslationAsync(string originalText, TagHelperOutput output, string? forumId, string? threadId, string? messageId)
    {
        // Determine content type and ID based on which attribute is present
        string? contentType = null;
        string? contentId = null;

        if (!string.IsNullOrWhiteSpace(forumId))
        {
            contentType = "Forum";
            contentId = forumId;
        }
        else if (!string.IsNullOrWhiteSpace(threadId))
        {
            contentType = "Thread";
            contentId = threadId;
        }
        else if (!string.IsNullOrWhiteSpace(messageId))
        {
            contentType = "Message";
            contentId = messageId;
        }

        // If no content type/ID found, fall back to UI string translation
        if (string.IsNullOrWhiteSpace(contentType) || string.IsNullOrWhiteSpace(contentId))
        {
            await ProcessUiStringTranslationAsync(originalText, output);
            return;
        }

        // Get current user's language
        var targetLanguage = _translator.GetCurrentLanguage();

        // Remove the auto-translate attribute
        output.Attributes.RemoveAll("auto-translate");

        // If English, no need to translate - just render original
        if (targetLanguage.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Try to get existing translation (non-blocking)
        string? translatedText = null;
        try
        {
            translatedText = await _contentTranslationService.GetTranslationAsync(
                contentType,
                contentId,
                TranslationField!,
                targetLanguage
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get translation for {ContentType}:{ContentId}.{Field}",
                contentType, contentId, TranslationField);
        }

        // If we have a translation, use it
        if (!string.IsNullOrWhiteSpace(translatedText))
        {
            // For content translations, encode HTML and preserve line breaks
            var encoded = System.Net.WebUtility.HtmlEncode(translatedText).Replace("\n", "<br/>");
            output.Content.SetHtmlContent(encoded);
        }
        else
        {
            // Queue translation in background with a new scope (fire-and-forget)
            // This avoids DbContext concurrency issues by creating a new service scope
            _ = Task.Run(async () =>
            {
                try
                {
                    // Create a new scope to avoid DbContext concurrency issues
                    using var scope = _serviceProvider.CreateScope();
                    var scopedTranslationService = scope.ServiceProvider.GetRequiredService<IContentTranslationService>();

                    await scopedTranslationService.TranslateContentAsync(
                        contentType,
                        contentId,
                        TranslationField!,
                        originalText,
                        targetLanguage
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background translation failed for {ContentType}:{ContentId}.{Field}",
                        contentType, contentId, TranslationField);
                }
            });
            // Keep original text while translation is pending
        }
    }
}
