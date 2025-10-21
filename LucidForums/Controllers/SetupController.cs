using LucidForums.Services.Seeding;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

public class SetupController(IForumSeedingQueue queue, ISeedingProgressStore progress, Services.Ai.ITextAiService ai) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    public record StartRequest(string ForumName, string? Description, int ThreadCount = 3, int RepliesPerThread = 2, string? SitePurpose = null, string? CharterDescription = null)
    {
        public string Slug => (ForumName ?? "sample-forum").Trim().ToLowerInvariant().Replace(" ", "-");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(StartRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ForumName))
        {
            ModelState.AddModelError("ForumName", "Forum name is required");
            Response.StatusCode = 400;
            return View("Index");
        }

        var jobId = Guid.NewGuid();
        var job = new ForumSeedingRequest(jobId, req.ForumName, req.Slug, req.Description, Math.Clamp(req.ThreadCount,1,20), Math.Clamp(req.RepliesPerThread,0,20), req.SitePurpose, req.CharterDescription);
        await queue.EnqueueAsync(job, ct);

        // If htmx, return polling fragment; else return JSON
        if (Request.Headers.ContainsKey("HX-Request"))
        {
            Response.Headers["HX-Trigger"] = "setup-seeding-started";
            var html = $@"<div id=""progressPoller"" hx-get=""/Setup/Progress?jobId={jobId}"" hx-trigger=""load, every 1s"" hx-target=""#progress"" hx-swap=""innerHTML"">Seeding job started…</div>";
            return Content(html, "text/html");
        }

        return Json(new { jobId });
    }

    [HttpGet]
    public IActionResult Progress(Guid jobId)
    {
        var items = progress.Get(jobId);
        var done = progress.IsComplete(jobId);
        var builder = new System.Text.StringBuilder();
        builder.Append("<div id=\"progress\">");
        builder.Append("<ul class=\"text-sm space-y-1\">");
        foreach (var e in items)
        {
            var cls = e.Stage switch { "error" => "text-red-600", "done" => "text-green-700", _ => "" };
            builder.Append($"<li class=\"{cls}\"><span class=\"font-mono text-xs text-gray-500\">[{e.TimestampUtc:HH:mm:ss}]</span> <b>{e.Stage}</b>: {System.Net.WebUtility.HtmlEncode(e.Message)}</li>");
        }
        builder.Append("</ul>");
        if (!done)
        {
            // Keep polling by returning a poller element that replaces itself
            builder.Append($"<div id=\"progressPoller\" hx-get=\"/Setup/Progress?jobId={jobId}\" hx-trigger=\"every 1s\" hx-target=\"#progress\" hx-swap=\"outerHTML\"></div>");
        }
        else
        {
            builder.Append("<div class=\"mt-2 text-sm text-gray-500\">Seeding complete.</div>");
        }
        builder.Append("</div>");
        return Content(builder.ToString(), "text/html");
    }

    // --- AI generation endpoints ---
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenForumName([FromForm] string? Description, [FromForm] string? SitePurpose, CancellationToken ct)
    {
        var charter = new LucidForums.Models.Entities.Charter { Name = "Forum Setup", Purpose = SitePurpose ?? Description ?? "Generate an appealing forum name" };
        var prompt = $"Suggest a concise, appealing forum name. Context: Description='{Description}'. Purpose='{SitePurpose}'. Return only the name, no quotes.";
        var text = await ai.GenerateAsync(charter, prompt, ct: ct);
        var value = System.Net.WebUtility.HtmlEncode(text?.Trim());
        return Content($"<input id=\"ForumName\" name=\"ForumName\" class=\"input-md\" value=\"{value}\" placeholder=\"e.g., Dreaming & Lucid Exploration\" required />", "text/html");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenDescription([FromForm] string ForumName, [FromForm] string? SitePurpose, CancellationToken ct)
    {
        var charter = new LucidForums.Models.Entities.Charter { Name = ForumName, Purpose = SitePurpose ?? "Generate forum description" };
        var prompt = $"Write a one-sentence forum description for '{ForumName}'. Purpose: '{SitePurpose}'. Keep it friendly and under 160 characters. Return only the sentence.";
        var text = await ai.GenerateAsync(charter, prompt, ct: ct);
        var value = System.Net.WebUtility.HtmlEncode(text?.Trim());
        return Content($"<input id=\"Description\" name=\"Description\" class=\"input-md\" value=\"{value}\" placeholder=\"A cozy space to discuss lucid dreams and techniques\" />", "text/html");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenSitePurpose([FromForm] string ForumName, [FromForm] string? Description, CancellationToken ct)
    {
        var charter = new LucidForums.Models.Entities.Charter { Name = ForumName, Purpose = "Define forum purpose" };
        var prompt = $"Based on forum name '{ForumName}' and description '{Description}', write a short phrase describing the site purpose. Return a brief phrase.";
        var text = await ai.GenerateAsync(charter, prompt, ct: ct);
        var value = System.Net.WebUtility.HtmlEncode(text?.Trim());
        return Content($"<input id=\"SitePurpose\" name=\"SitePurpose\" class=\"input-md\" value=\"{value}\" placeholder=\"Brief description of what this forum is about\" />", "text/html");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenCharter([FromForm] string ForumName, [FromForm] string? Description, [FromForm] string? SitePurpose, CancellationToken ct)
    {
        var charter = new LucidForums.Models.Entities.Charter { Name = ForumName, Purpose = SitePurpose ?? Description ?? "Guidelines" };
        var prompt = $"Write a 1-2 sentence charter/guidelines description setting tone and rules for the forum '{ForumName}'. Consider: '{Description}' and purpose '{SitePurpose}'. Keep it concise.";
        var text = await ai.GenerateAsync(charter, prompt, ct: ct);
        var value = System.Net.WebUtility.HtmlEncode(text?.Trim());
        return Content($"<input id=\"CharterDescription\" name=\"CharterDescription\" class=\"input-md\" value=\"{value}\" placeholder=\"Guidelines and tone for generated content\" />", "text/html");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenAllAndStart([FromForm] int ThreadCount = 3, [FromForm] int RepliesPerThread = 2, CancellationToken ct = default)
    {
        // Generate sensible defaults
        var charter = new LucidForums.Models.Entities.Charter { Name = "Forum Setup", Purpose = "Generate forum seed" };
        var name = await ai.GenerateAsync(charter, "Suggest a concise, appealing forum name about lucid dreaming & exploration. Return only the name.", ct: ct);
        var forumName = string.IsNullOrWhiteSpace(name) ? "Dreamers Hub" : name.Trim();
        var desc = await ai.GenerateAsync(new LucidForums.Models.Entities.Charter { Name = forumName, Purpose = "Description" }, $"One-sentence description for '{forumName}', under 160 chars.", ct: ct);
        var sitePurpose = await ai.GenerateAsync(new LucidForums.Models.Entities.Charter { Name = forumName, Purpose = "Purpose" }, $"Short phrase describing the site purpose for '{forumName}'.", ct: ct);
        var charterDesc = await ai.GenerateAsync(new LucidForums.Models.Entities.Charter { Name = forumName, Purpose = "Charter" }, $"1-2 sentence charter setting tone and rules for '{forumName}'.", ct: ct);

        var req = new StartRequest(forumName, desc, ThreadCount, RepliesPerThread, sitePurpose, charterDesc);
        var jobId = Guid.NewGuid();
        var job = new ForumSeedingRequest(jobId, req.ForumName, req.Slug, req.Description, Math.Clamp(req.ThreadCount,1,20), Math.Clamp(req.RepliesPerThread,0,20), req.SitePurpose, req.CharterDescription);
        await queue.EnqueueAsync(job, ct);

        var html = $@"<div id=""progress""><div class=""text-sm"">Generated forum '<b>{System.Net.WebUtility.HtmlEncode(forumName)}</b>' – starting seeding…</div><div id=""progressPoller"" hx-get=""/Setup/Progress?jobId={jobId}"" hx-trigger=""load, every 1s"" hx-target=""#progress"" hx-swap=""innerHTML""></div></div>";
        return Content(html, "text/html");
    }
}
