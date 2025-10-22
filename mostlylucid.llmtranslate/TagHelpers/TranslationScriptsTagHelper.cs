using Microsoft.AspNetCore.Razor.TagHelpers;

namespace mostlylucid.llmtranslate.TagHelpers;

/// <summary>
/// Tag helper for including translation JavaScript and SignalR connection
/// Usage: <translation-scripts signalr-hub="/hubs/translation" />
/// Place this at the end of your layout, before closing </body> tag
/// </summary>
[HtmlTargetElement("translation-scripts")]
public class TranslationScriptsTagHelper : TagHelper
{
    /// <summary>
    /// SignalR hub path (default: /hubs/translation)
    /// </summary>
    [HtmlAttributeName("signalr-hub")]
    public string SignalRHub { get; set; } = "/hubs/translation";

    /// <summary>
    /// Whether to include SignalR for real-time updates (default: true)
    /// </summary>
    [HtmlAttributeName("include-signalr")]
    public bool IncludeSignalR { get; set; } = true;

    /// <summary>
    /// Whether to enable debug logging (default: false)
    /// </summary>
    [HtmlAttributeName("debug")]
    public bool Debug { get; set; } = false;

    /// <summary>
    /// Whether to enable toast notifications (default: true)
    /// </summary>
    [HtmlAttributeName("enable-notifications")]
    public bool EnableNotifications { get; set; } = true;

    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null; // Remove the tag itself

        var signalrCdn = IncludeSignalR ? @"
<!-- SignalR for real-time translation updates -->
<script src=""https://cdn.jsdelivr.net/npm/@microsoft/signalr@latest/dist/browser/signalr.min.js""></script>" : "";

        var html = $@"{signalrCdn}

<!-- Translation System Bundle -->
<script>
// Configure translation manager
window.translationConfig = {{
    debug: {Debug.ToString().ToLowerInvariant()},
    signalRHub: '{SignalRHub}',
    enableNotifications: {EnableNotifications.ToString().ToLowerInvariant()},
    enableSignalR: {IncludeSignalR.ToString().ToLowerInvariant()}
}};
</script>
<script src=""/js/translation-bundle.js""></script>
";

        output.Content.SetHtmlContent(html);
        return Task.CompletedTask;
    }
}
