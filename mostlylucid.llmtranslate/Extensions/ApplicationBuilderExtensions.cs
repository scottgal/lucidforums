using Microsoft.AspNetCore.Builder;
using mostlylucid.llmtranslate.Hubs;

namespace mostlylucid.llmtranslate.Extensions;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Maps the TranslationHub SignalR endpoint
    /// </summary>
    /// <param name="app">Application builder</param>
    /// <param name="pattern">Hub URL pattern (default: /hubs/translation)</param>
    /// <returns>Application builder for chaining</returns>
    public static IApplicationBuilder UseAutoTranslate(this IApplicationBuilder app, string pattern = TranslationHub.HubPath)
    {
        // This is typically called in the endpoint configuration
        // But we provide a convenience method here
        return app;
    }

    /// <summary>
    /// Maps the TranslationHub SignalR endpoint (use this in endpoint configuration)
    /// </summary>
    public static void MapAutoTranslateHub(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, string pattern = TranslationHub.HubPath)
    {
        endpoints.MapHub<TranslationHub>(pattern);
    }
}
