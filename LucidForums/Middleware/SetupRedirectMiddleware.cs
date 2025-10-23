using LucidForums.Services.Setup;

namespace LucidForums.Middleware;

/// <summary>
/// Middleware that redirects all requests to the initial setup page if no admin users exist
/// </summary>
public class SetupRedirectMiddleware
{
    private readonly RequestDelegate _next;

    public SetupRedirectMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ISetupService setupService)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

        // Allow setup routes, static files, and SignalR hubs to pass through
        if (path.StartsWith("/initial-setup") ||
            path.StartsWith("/css") ||
            path.StartsWith("/js") ||
            path.StartsWith("/lib") ||
            path.StartsWith("/hubs") ||
            path.StartsWith("/_framework") ||
            path.Contains("."))  // Static files with extensions
        {
            await _next(context);
            return;
        }

        // Check if setup is required
        var requiresSetup = await setupService.RequiresSetupAsync(context.RequestAborted);

        if (requiresSetup && !path.StartsWith("/initial-setup"))
        {
            // Redirect to setup page
            context.Response.Redirect("/initial-setup");
            return;
        }

        await _next(context);
    }
}

/// <summary>
/// Extension methods for SetupRedirectMiddleware
/// </summary>
public static class SetupRedirectMiddlewareExtensions
{
    public static IApplicationBuilder UseSetupRedirect(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SetupRedirectMiddleware>();
    }
}
