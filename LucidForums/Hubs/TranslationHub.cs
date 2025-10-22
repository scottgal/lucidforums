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
