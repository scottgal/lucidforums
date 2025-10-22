using LucidForums.Helpers;
using LucidForums.Services.Seeding;
using Microsoft.AspNetCore.Mvc;
using LucidForums.Data;
using Microsoft.EntityFrameworkCore;
using LucidForums.Extensions;

namespace LucidForums.Controllers;

public class SetupController(IForumSeedingQueue queue, ISeedingProgressStore progress, Services.Ai.ITextAiService ai, ApplicationDbContext db, TranslationHelper translator) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        if (Request.IsHtmxRequest())
            return PartialView("_SetupForm");
        return View();
    }

    public record StartRequest(string ForumName, string? Description, int ThreadCount = 3, int RepliesPerThread = 2, string? SitePurpose = null, string? CharterDescription = null, bool IncludeEmoticons = false)
    {
        public string Slug => (ForumName ?? "sample-forum").Trim().ToLowerInvariant().Replace(" ", "-");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(StartRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ForumName))
        {
            var errorMessage = await translator.T("setup.forum-name.required", "Forum name is required");
            ModelState.AddModelError("ForumName", errorMessage);
            Response.StatusCode = 400;
            return View("Index");
        }

        var jobId = Guid.NewGuid();
        var job = new ForumSeedingRequest(jobId, req.ForumName, req.Slug, req.Description, Math.Clamp(req.ThreadCount,1,20), Math.Clamp(req.RepliesPerThread,0,20), req.SitePurpose, req.CharterDescription, req.IncludeEmoticons);
        await queue.EnqueueAsync(job, ct);

        // If htmx, return polling fragment; else return JSON
        if (Request.IsHtmxRequest())
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
        var prompt = $"Suggest a concise, appealing forum name. Context: Description='{Description}'. Purpose='{SitePurpose}'. Avoid overused names like 'Pixel Playground'. Return only the name, no quotes.";
        var text = await ai.GenerateAsync(charter, prompt, ct: ct);
        var suggestion = (text ?? string.Empty).Trim();
        if (suggestion.Equals("Pixel Playground", StringComparison.OrdinalIgnoreCase))
        {
            // Ask again explicitly excluding the cliché if the model still returned it
            var retry = await ai.GenerateAsync(charter, "Suggest a different concise, appealing forum name that is not 'Pixel Playground'. Return only the name.", ct: ct);
            if (!string.IsNullOrWhiteSpace(retry)) suggestion = retry.Trim();
        }
        var value = System.Net.WebUtility.HtmlEncode(suggestion);
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
    public async Task<IActionResult> GenAllAndStart([FromForm] int forums = 10, [FromForm] bool IncludeEmoticons = true, CancellationToken ct = default)
    {
        // One-click: generate multiple forums with diversity vs existing names
        var existingNames = await db.Forums.AsNoTracking().Select(f => f.Name).ToListAsync(ct);
        var existingSlugs = new HashSet<string>(existingNames.Select(Slugify), StringComparer.OrdinalIgnoreCase);
        var created = new List<(Guid JobId, string ForumName)>();

        static string Slugify(string? s)
        {
            s ??= string.Empty;
            var slug = new string(s.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
            while (slug.Contains("--")) slug = slug.Replace("--", "-");
            return slug.Trim('-');
        }

        int toCreate = Math.Clamp(forums, 1, 20);
        for (int i = 0; i < toCreate; i++)
        {
            ct.ThrowIfCancellationRequested();
            var setupCharter = new LucidForums.Models.Entities.Charter { Name = "Forum Setup", Purpose = "Generate forum seed" };

            string forumName = string.Empty;
            const int maxAttempts = 6;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var avoidList = string.Join(", ", existingNames.Take(50)); // keep prompt short
                var prompt = $"Suggest a concise, appealing and distinct forum name. Avoid names similar to: {avoidList}. Make it niche but fun. Return only the name.";
                var name = await ai.GenerateAsync(setupCharter, prompt, ct: ct);
                forumName = string.IsNullOrWhiteSpace(name) ? $"Forum {Guid.NewGuid().ToString()[..6]}" : name.Trim();
                if (forumName.Equals("Pixel Playground", StringComparison.OrdinalIgnoreCase))
                {
                    var retry = await ai.GenerateAsync(setupCharter, "Suggest a different concise, appealing forum name that is not 'Pixel Playground'. Return only the name.", ct: ct);
                    if (!string.IsNullOrWhiteSpace(retry)) forumName = retry.Trim();
                }
                var slug = Slugify(forumName);
                if (!existingSlugs.Contains(slug) && !created.Any(c => Slugify(c.ForumName) == slug))
                {
                    existingNames.Add(forumName);
                    existingSlugs.Add(slug);
                    break;
                }
                // add a variation hint next attempt
                setupCharter.Purpose = "Generate a different name variant";
            }
            if (string.IsNullOrWhiteSpace(forumName)) forumName = $"Community {i+1}";

            var desc = await ai.GenerateAsync(new LucidForums.Models.Entities.Charter { Name = forumName, Purpose = "Description" }, $"One-sentence description for '{forumName}', under 160 chars.", ct: ct);
            var sitePurpose = await ai.GenerateAsync(new LucidForums.Models.Entities.Charter { Name = forumName, Purpose = "Purpose" }, $"Short phrase describing the site purpose for '{forumName}'.", ct: ct);
            var charterDesc = await ai.GenerateAsync(new LucidForums.Models.Entities.Charter { Name = forumName, Purpose = "Charter" }, $"1-2 sentence charter setting tone and rules for '{forumName}'.", ct: ct);

            var rnd = System.Random.Shared;
            int threadCount = rnd.Next(10, 51); // 10..50 inclusive-ish
            int repliesPerThread = rnd.Next(0, 21); // 0..20

            var req = new StartRequest(forumName, desc, threadCount, repliesPerThread, sitePurpose, charterDesc, IncludeEmoticons);
            var jobId = Guid.NewGuid();
            var job = new ForumSeedingRequest(jobId, req.ForumName, req.Slug, req.Description, Math.Clamp(req.ThreadCount,1,50), Math.Clamp(req.RepliesPerThread,0,20), req.SitePurpose, req.CharterDescription, req.IncludeEmoticons);
            await queue.EnqueueAsync(job, ct);
            created.Add((jobId, forumName));
        }

        // Build a composite progress panel with one poller per job
        var sb = new System.Text.StringBuilder();
        sb.Append("<div id=\"progress\">");
        sb.Append("<div class=\"text-sm mb-2\">Generating ");
        sb.Append(created.Count);
        sb.Append(" forums – starting seeding…</div>");
        sb.Append("<ul class=\"space-y-1\">");
        foreach (var c in created)
        {
            var safeName = System.Net.WebUtility.HtmlEncode(c.ForumName);
            sb.Append($"<li><div class=\"text-xs text-gray-700\"><b>{safeName}</b></div><div class=\"border rounded p-1 mt-1\" id=\"job-{c.JobId}\" hx-get=\"/Setup/Progress?jobId={c.JobId}\" hx-trigger=\"load, every 2s\" hx-target=\"#job-{c.JobId}\" hx-swap=\"innerHTML\">Queued…</div></li>");
        }
        sb.Append("</ul></div>");
        return Content(sb.ToString(), "text/html");
    }
}
