using Microsoft.AspNetCore.Razor.TagHelpers;
using mostlylucid.llmtranslate.Helpers;

namespace mostlylucid.llmtranslate.TagHelpers;

/// <summary>
/// Tag helper for displaying translation status/progress
/// Usage: <translation-status />
/// </summary>
[HtmlTargetElement("translation-status")]
public class TranslationStatusTagHelper : TagHelper
{
    private readonly TranslationHelper _translationHelper;

    /// <summary>
    /// Position of the status indicator
    /// Options: "top-right", "top-left", "bottom-right", "bottom-left", "inline"
    /// Default: "top-right"
    /// </summary>
    [HtmlAttributeName("position")]
    public string Position { get; set; } = "top-right";

    /// <summary>
    /// Whether to show detailed stats (percentage, count, etc.)
    /// Default: false
    /// </summary>
    [HtmlAttributeName("show-details")]
    public bool ShowDetails { get; set; } = false;

    public TranslationStatusTagHelper(TranslationHelper translationHelper)
    {
        _translationHelper = translationHelper;
    }

    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var currentLang = _translationHelper.GetCurrentLanguage();
        var positionClass = Position switch
        {
            "top-right" => "position-fixed top-0 end-0 m-3",
            "top-left" => "position-fixed top-0 start-0 m-3",
            "bottom-right" => "position-fixed bottom-0 end-0 m-3",
            "bottom-left" => "position-fixed bottom-0 start-0 m-3",
            "inline" => "",
            _ => "position-fixed top-0 end-0 m-3"
        };

        output.TagName = "div";
        output.Attributes.SetAttribute("id", "translation-status");
        output.Attributes.SetAttribute("class", $"translation-status {positionClass}");

        var html = $@"
<div class=""card"" style=""min-width: 150px;"">
    <div class=""card-body p-2"">
        <div class=""d-flex align-items-center gap-2"">
            <span class=""badge bg-primary"" id=""current-lang"">{currentLang.ToUpperInvariant()}</span>
            <span id=""translation-loading-indicator"" class=""spinner-border spinner-border-sm d-none"" role=""status"">
                <span class=""visually-hidden"">Loading...</span>
            </span>
        </div>
        {(ShowDetails ? @"
        <div id=""translation-progress"" class=""mt-2 d-none"">
            <div class=""progress"" style=""height: 5px;"">
                <div id=""translation-progress-bar"" class=""progress-bar"" role=""progressbar"" style=""width: 0%""></div>
            </div>
            <small id=""translation-progress-text"" class=""text-muted""></small>
        </div>" : "")}
    </div>
</div>

<style>
.translation-status {{
    z-index: 1050;
    transition: all 0.3s ease;
}}
.translation-status .card {{
    box-shadow: 0 2px 10px rgba(0,0,0,0.1);
}}
</style>
";

        output.Content.SetHtmlContent(html);

        return Task.CompletedTask;
    }
}
