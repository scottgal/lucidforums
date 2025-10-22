using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Extensions;

/// <summary>
/// Extension methods for working with HTMX requests
/// </summary>
public static class HtmxExtensions
{
    /// <summary>
    /// Determines if the current request is an HTMX request
    /// </summary>
    public static bool IsHtmxRequest(this HttpRequest request)
    {
        return request.Headers.ContainsKey("HX-Request");
    }

    /// <summary>
    /// Determines if the current request is an HTMX boosted request
    /// </summary>
    public static bool IsHtmxBoosted(this HttpRequest request)
    {
        return request.Headers.ContainsKey("HX-Boosted");
    }

    /// <summary>
    /// Gets the HTMX trigger element ID if available
    /// </summary>
    public static string? GetHtmxTrigger(this HttpRequest request)
    {
        return request.Headers.TryGetValue("HX-Trigger", out var value) ? value.ToString() : null;
    }

    /// <summary>
    /// Gets the HTMX target element ID if available
    /// </summary>
    public static string? GetHtmxTarget(this HttpRequest request)
    {
        return request.Headers.TryGetValue("HX-Target", out var value) ? value.ToString() : null;
    }

    /// <summary>
    /// Gets the HTMX current URL if available
    /// </summary>
    public static string? GetHtmxCurrentUrl(this HttpRequest request)
    {
        return request.Headers.TryGetValue("HX-Current-URL", out var value) ? value.ToString() : null;
    }

    /// <summary>
    /// Returns a partial view if HTMX request, otherwise returns full view
    /// </summary>
    public static IActionResult ViewOrPartial(this Controller controller, string? viewName = null, object? model = null, string? partialViewName = null)
    {
        if (controller.Request.IsHtmxRequest())
        {
            return controller.PartialView(partialViewName ?? viewName, model);
        }
        return controller.View(viewName, model);
    }

    /// <summary>
    /// Returns a partial view if HTMX request, otherwise returns full view
    /// </summary>
    public static IActionResult ViewOrPartial(this Controller controller, object? model, string? partialViewName = null)
    {
        if (controller.Request.IsHtmxRequest())
        {
            return controller.PartialView(partialViewName, model);
        }
        return controller.View(model);
    }
}
