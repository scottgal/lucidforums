using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using LucidForums.Services.Ai;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

public class EditorController(ITextAiService textAiService) : Controller
{
    // GET: /Editor
    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Editor";
        return View();
    }

    // Returns the editor partial so HTMX can swap it if needed
    [HttpGet]
    public IActionResult _Editor()
    {
        return PartialView();
    }

    // HTMX endpoint to render a preview of the text
    [HttpPost]
    public IActionResult Preview([FromForm] string? text)
    {
        text ??= string.Empty;
        // Minimal safe preview: convert newlines to <br> and basic link formatting
        // In a real app, replace with proper Markdown renderer.
        var encoded = System.Net.WebUtility.HtmlEncode(text);
        var html = encoded
            .Replace("\r\n", "\n")
            .Replace("\n", "<br/>");
        return PartialView("_Preview", html);
    }

    // HTMX endpoint to return character count
    [HttpPost]
    public IActionResult Count([FromForm] string? text)
    {
        var count = (text ?? string.Empty).Length;
        return Content(count.ToString());
    }

    // Suggest categories for the provided text using the LLM
    public record CategorizeRequest(string? Text, int? Max = null);
    public record CategorizeResponse(string[] Categories);

    [HttpPost]
    public async Task<IActionResult> SuggestCategories([FromBody] CategorizeRequest req, CancellationToken ct)
    {
        var text = req.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return Json(new CategorizeResponse(Array.Empty<string>()));
        }

        var max = req.Max.HasValue && req.Max.Value > 0 ? Math.Min(req.Max.Value, 10) : 5;

        var charter = new LucidForums.Models.Entities.Charter
        {
            Name = "Content Tagger",
            Purpose = "Given user content, produce a concise list of topical categories suitable for tagging a forum post."
        };

        var prompt = new StringBuilder()
            .AppendLine("Task: Return ONLY a comma-separated list of 1-7 concise topical categories (no emojis, no hashtags, no extra words).")
            .AppendLine($"Aim for up to {max} categories that are useful for navigation.")
            .AppendLine("Do not include any explanations or numbering. Output a single line.")
            .AppendLine()
            .AppendLine("Content:")
            .AppendLine(text)
            .ToString();

        var output = await textAiService.GenerateAsync(charter, prompt, null, ct);
        output = output?.Trim() ?? string.Empty;

        // Normalize to a single line, split on commas or newlines, strip bullets/numbering
        var firstLine = output.Split('\n').FirstOrDefault() ?? output;
        var pieces = firstLine
            .Replace(';', ',')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Regex.Replace(s, @"^[-*]\s*", string.Empty))
            .Select(s => Regex.Replace(s, @"^\d+[.)]\s*", string.Empty))
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToArray();

        return Json(new CategorizeResponse(pieces));
    }

    // SSE endpoint to stream translation
    [HttpGet]
    public async Task TranslateStream([FromQuery] string text, [FromQuery] string lang, [FromQuery] string? source, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.ContentType = "text/event-stream";

        async Task WriteSse(string data)
        {
            var payload = $"data: {data.Replace("\r", string.Empty).Replace("\n", "\\n")}\n\n";
            var bytes = Encoding.UTF8.GetBytes(payload);
            await Response.Body.WriteAsync(bytes, 0, bytes.Length, ct);
            await Response.Body.FlushAsync(ct);
        }

        // Signal client to clear previous text
        await WriteSse("__reset__");

        await textAiService.TranslateStreamAsync(text ?? string.Empty, lang ?? "English", source, async chunk =>
        {
            await WriteSse(chunk);
        }, ct);
    }
}
