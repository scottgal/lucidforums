using Microsoft.AspNetCore.SignalR;

namespace LucidForums.Hubs;

public class ForumHub : Hub
{
    public const string HubPath = "/hubs/forum";

    public async Task JoinThread(string threadId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(threadId));
    }

    public async Task LeaveThread(string threadId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(threadId));
    }

    public async Task JoinHome()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "home");
    }

    public async Task LeaveHome()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "home");
    }

    public static string GroupName(string threadId) => $"thread:{threadId}";
}