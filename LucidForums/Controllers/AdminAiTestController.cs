using System.Text;
using LucidForums.Services.Ai;
using LucidForums.Services.Charters;
using LucidForums.Services.Forum;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

// Simple admin AI test surface. If your app has authorization/roles, consider decorating with [Authorize(Roles="Admin")]
[Route("Admin/AiTest")] 
public class AdminAiTestController(ITextAiService ai, ICharterService charters, IThreadViewService threadViews) : Controller
{
    public class IndexVm
    {
        public List<LucidForums.Models.Entities.Charter> CharterOptions { get; set; } = new();
        public Guid? SelectedCharterId { get; set; }
        public string? ModelName { get; set; }
        public string? Prompt { get; set; }
        public string? Output { get; set; }
        // Optional context for reply simulation
        public Guid? ThreadId { get; set; }
        public int ContextMessageLimit { get; set; } = 5;
        public string? ReplySuggestion { get; set; }
        public string? Error { get; set; }
    }

    [HttpGet]
    [Route("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new IndexVm
        {
            CharterOptions = await charters.ListAsync(ct)
        };
        ViewData["Title"] = "Admin • AI Test";
        return View(vm);
    }

    public record RunRequest(Guid? CharterId, string? Prompt, string? ModelName);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Run")]
    public async Task<IActionResult> Run([FromForm] RunRequest req, CancellationToken ct)
    {
        var vm = new IndexVm
        {
            CharterOptions = await charters.ListAsync(ct),
            SelectedCharterId = req.CharterId,
            Prompt = req.Prompt,
            ModelName = req.ModelName
        };

        if (string.IsNullOrWhiteSpace(req.Prompt))
        {
            vm.Error = "Please enter a prompt to run.";
            Response.StatusCode = 400;
            return View("Index", vm);
        }

        var charter = req.CharterId.HasValue
            ? await charters.GetByIdAsync(req.CharterId.Value, ct)
            : null;

        charter ??= new LucidForums.Models.Entities.Charter
        {
            Name = "General Assistant",
            Purpose = "Be a concise, helpful assistant for forum admins testing AI behavior."
        };

        vm.Output = await ai.GenerateAsync(charter!, req.Prompt!, req.ModelName, ct);
        return View("Index", vm);
    }

    public record ReplyRequest(Guid? ThreadId, int? ContextMessageLimit, string? Prompt, Guid? CharterId);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("SuggestReply")]
    public async Task<IActionResult> SuggestReply([FromForm] ReplyRequest req, CancellationToken ct)
    {
        var vm = new IndexVm
        {
            CharterOptions = await charters.ListAsync(ct),
            SelectedCharterId = req.CharterId,
            Prompt = req.Prompt,
            ThreadId = req.ThreadId,
            ContextMessageLimit = Math.Clamp(req.ContextMessageLimit ?? 5, 1, 25)
        };

        if (!req.ThreadId.HasValue)
        {
            vm.Error = "Provide a ThreadId to simulate a reply.";
            Response.StatusCode = 400;
            return View("Index", vm);
        }

        var view = await threadViews.GetViewAsync(req.ThreadId.Value, ct);
        if (view == null)
        {
            vm.Error = "Thread not found.";
            Response.StatusCode = 404;
            return View("Index", vm);
        }

        // Build compact context of last N messages
        var contextMsgs = view.Messages
            .OrderBy(m => m.CreatedAtUtc)
            .TakeLast(vm.ContextMessageLimit)
            .Select(m => $"[{m.CreatedAtUtc:u}] {(m.AuthorId ?? "anon")} (depth {m.Depth}): {m.Content}");

        var sb = new StringBuilder();
        sb.AppendLine("You are assisting in a forum thread. Based on the context, propose a helpful, concise reply.");
        sb.AppendLine("Do not include offensive content. Keep it under 120 words unless more detail is clearly necessary.");
        sb.AppendLine();
        sb.AppendLine("Context:");
        foreach (var line in contextMsgs)
            sb.AppendLine(line);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(req.Prompt))
        {
            sb.AppendLine("Additional admin instruction:");
            sb.AppendLine(req.Prompt);
        }

        var charter = req.CharterId.HasValue
            ? await charters.GetByIdAsync(req.CharterId.Value, ct)
            : new LucidForums.Models.Entities.Charter
            {
                Name = "Forum Reply Assistant",
                Purpose = "Generate a helpful reply grounded in provided thread context."
            };

        vm.ReplySuggestion = await ai.GenerateAsync(charter!, sb.ToString(), null, ct);
        return View("Index", vm);
    }
}