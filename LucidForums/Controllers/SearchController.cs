using LucidForums.Data;
using LucidForums.Models.ViewModels;
using LucidForums.Services.Forum;
using LucidForums.Services.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Controllers;

public class SearchController(IEmbeddingService embeddingService, IForumService forumService, ApplicationDbContext db) : Controller
{
    [HttpGet]
    [Route("Search")] // Global search
    public async Task<IActionResult> Index([FromQuery] string q, CancellationToken ct)
    {
        var query = q ?? string.Empty;
        var matches = await embeddingService.SearchAsync(query, forumId: null, limit: 20, ct);
        var messageIds = matches.Select(m => m.MessageId).ToList();
        var msgs = await db.Messages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .Select(m => new { m.Id, m.ForumThreadId, m.Content })
            .ToListAsync(ct);
        var threads = await db.Threads.AsNoTracking()
            .Where(t => msgs.Select(m => m.ForumThreadId).Contains(t.Id))
            .Select(t => new { t.Id, t.ForumId })
            .ToListAsync(ct);
        var forums = await db.Forums.AsNoTracking()
            .Where(f => threads.Select(t => t.ForumId).Contains(f.Id))
            .Select(f => new { f.Id, f.Slug })
            .ToListAsync(ct);

        var results = (from m in matches
                       join msg in msgs on m.MessageId equals msg.Id
                       join th in threads on msg.ForumThreadId equals th.Id
                       join fo in forums on th.ForumId equals fo.Id
                       select new SearchResultVm(fo.Slug, fo.Id, th.Id, msg.Id, BuildSnippet(msg.Content, q), m.Score)).ToList();
        var vm = new SearchPageVm(null, query, results);
        return View(vm);
    }

    [HttpGet]
    [Route("Forum/{slug}/Search")] // Forum-scoped search
    public async Task<IActionResult> Forum(string slug, [FromQuery] string q, CancellationToken ct)
    {
        var forum = await forumService.GetBySlugAsync(slug, ct);
        if (forum == null) return NotFound();
        var query = q ?? string.Empty;
        var matches = await embeddingService.SearchAsync(query, forum.Id, limit: 20, ct);
        var messageIds = matches.Select(m => m.MessageId).ToList();
        var msgs = await db.Messages.AsNoTracking()
            .Where(m => messageIds.Contains(m.Id))
            .Select(m => new { m.Id, m.ForumThreadId, m.Content })
            .ToListAsync(ct);
        var threads = await db.Threads.AsNoTracking()
            .Where(t => msgs.Select(m => m.ForumThreadId).Contains(t.Id))
            .Select(t => new { t.Id })
            .ToListAsync(ct);

        var results = (from m in matches
                       join msg in msgs on m.MessageId equals msg.Id
                       join th in threads on msg.ForumThreadId equals th.Id
                       select new SearchResultVm(forum.Slug, forum.Id, th.Id, msg.Id, BuildSnippet(msg.Content, q), m.Score)).ToList();
        var vm = new SearchPageVm(forum.Slug, query, results);
        return View("Index", vm);
    }

    private static string BuildSnippet(string content, string? query)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        if (string.IsNullOrEmpty(query)) return content.Length > 240 ? content.Substring(0, 240) + "…" : content;
        var idx = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return content.Length > 240 ? content.Substring(0, 240) + "…" : content;
        var start = Math.Max(0, idx - 60);
        var len = Math.Min(200, content.Length - start);
        var snippet = content.Substring(start, len);
        return (start > 0 ? "…" : "") + snippet + (start + len < content.Length ? "…" : "");
    }
}
