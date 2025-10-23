using Microsoft.AspNetCore.SignalR;

namespace LucidForums.Hubs;

public class SetupHub : Hub
{
    public static readonly string HubPath = "/hubs/setup";

    public async Task SendProgress(string message, int percentComplete)
    {
        await Clients.All.SendAsync("ProgressUpdate", message, percentComplete);
    }

    public async Task SendComplete(string message, object result)
    {
        await Clients.All.SendAsync("SetupComplete", message, result);
    }

    public async Task SendError(string message)
    {
        await Clients.All.SendAsync("SetupError", message);
    }
}
