using Microsoft.AspNetCore.SignalR;

namespace LucidForums.Hubs;

/// <summary>
/// SignalR hub for broadcasting translation progress and content translation updates
/// </summary>
public class TranslationHub : Hub
{
    public const string HubPath = "/hubs/translation";

    /// <summary>
    /// Broadcast translation progress to all clients
    /// </summary>
    public async Task BroadcastProgress(string jobId, int total, int completed, string? currentKey)
    {
        await Clients.All.SendAsync("TranslationProgress", new
        {
            JobId = jobId,
            Total = total,
            Completed = completed,
            CurrentKey = currentKey,
            Percentage = total > 0 ? (completed / (double)total) * 100.0 : 0
        });
    }

    /// <summary>
    /// Broadcast when a translation job completes
    /// </summary>
    public async Task BroadcastComplete(string jobId, int translatedCount)
    {
        await Clients.All.SendAsync("TranslationComplete", new
        {
            JobId = jobId,
            TranslatedCount = translatedCount
        });
    }

    /// <summary>
    /// Broadcast when a specific string is translated
    /// </summary>
    public async Task BroadcastStringTranslated(string key, string languageCode, string translatedText)
    {
        await Clients.All.SendAsync("StringTranslated", new
        {
            Key = key,
            LanguageCode = languageCode,
            TranslatedText = translatedText
        });
    }

    /// <summary>
    /// Broadcast HTMX OOB-compatible HTML fragment for UI string translation
    /// </summary>
    public async Task BroadcastStringTranslatedOOB(string key, string languageCode, string translatedText, string contentHash)
    {
        // Generate element ID using same pattern as tag helper
        var elementId = $"t-{contentHash.Substring(0, 8)}";

        // Create HTMX OOB swap fragment
        var html = $"<span id=\"{elementId}\" data-translate-key=\"{key}\" data-content-hash=\"{contentHash}\" data-translate-type=\"ui-string\" hx-swap-oob=\"true\">{System.Net.WebUtility.HtmlEncode(translatedText)}<span class=\"translate-progress\" style=\"display:none;\"><span class=\"loading loading-spinner loading-xs ml-1\"></span></span></span>";

        await Clients.All.SendAsync("TranslationOOBSwap", new
        {
            ElementId = elementId,
            Html = html,
            Key = key,
            LanguageCode = languageCode
        });
    }

    /// <summary>
    /// Broadcast HTMX OOB-compatible HTML fragment for content translation
    /// </summary>
    public async Task BroadcastContentTranslatedOOB(string contentType, string contentId, string fieldName, string languageCode, string translatedText, string contentHash)
    {
        // Generate element ID using same pattern as tag helper
        var elementId = $"content-{contentType}-{contentId}-{fieldName}";

        // Encode HTML and preserve line breaks
        var encoded = System.Net.WebUtility.HtmlEncode(translatedText).Replace("\n", "<br/>");

        // Create HTMX OOB swap fragment with all tracking attributes
        var html = $"<div id=\"{elementId}\" data-content-type=\"{contentType}\" data-content-id=\"{contentId}\" data-content-field=\"{fieldName}\" data-content-hash=\"{contentHash}\" data-translate-type=\"content\" hx-swap-oob=\"true\">{encoded}<span class=\"translate-progress\" style=\"display:none;\"><span class=\"loading loading-spinner loading-xs ml-1\"></span></span></div>";

        await Clients.All.SendAsync("TranslationOOBSwap", new
        {
            ElementId = elementId,
            Html = html,
            ContentType = contentType,
            ContentId = contentId,
            FieldName = fieldName,
            LanguageCode = languageCode
        });
    }

    /// <summary>
    /// Join a content-specific group to receive translation updates
    /// </summary>
    public async Task JoinContentGroup(string contentType, string contentId)
    {
        var groupName = GetGroupName(contentType, contentId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Leave a content-specific group
    /// </summary>
    public async Task LeaveContentGroup(string contentType, string contentId)
    {
        var groupName = GetGroupName(contentType, contentId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    public static string GetGroupName(string contentType, string contentId)
    {
        return $"{contentType}:{contentId}";
    }
}
