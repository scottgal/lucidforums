using LucidForums.Services.Seeding;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

public class SetupController(IForumSeedingQueue queue) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    public record StartRequest(string ForumName, string? Description, int ThreadCount = 3, int RepliesPerThread = 2)
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
        var job = new ForumSeedingRequest(jobId, req.ForumName, req.Slug, req.Description, Math.Clamp(req.ThreadCount,1,20), Math.Clamp(req.RepliesPerThread,0,20));
        await queue.EnqueueAsync(job, ct);
        // Return job id for client to subscribe
        return Json(new { jobId });
    }
}
