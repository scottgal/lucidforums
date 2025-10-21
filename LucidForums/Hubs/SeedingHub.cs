using Microsoft.AspNetCore.SignalR;

namespace LucidForums.Hubs;

public class SeedingHub : Hub
{
    public const string HubPath = "/hubs/seeding";

    public static string GroupName(Guid jobId) => $"seed:{jobId}";

    public async Task JoinJob(string jobId)
    {
        if (Guid.TryParse(jobId, out var id))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(id));
        }
    }

    public async Task LeaveJob(string jobId)
    {
        if (Guid.TryParse(jobId, out var id))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(id));
        }
    }
}
