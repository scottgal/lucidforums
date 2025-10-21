using System.Text;
using System.Linq;
using LucidForums.Services.Ai;
using LucidForums.Services.Charters;
using LucidForums.Services.Forum;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

// Simple admin AI test surface. If your app has authorization/roles, consider decorating with [Authorize(Roles="Admin")]
[Route("Admin/AiTest")] 
public class AdminAiTestController(ITextAiService ai, ICharterService charters, IThreadViewService threadViews, IEnumerable<IChatProvider> chatProviders, IAiSettingsService aiSettings, LucidForums.Services.Search.IEmbeddingService embeddings) : Controller
{
    public class IndexVm
    {
        public List<LucidForums.Models.Entities.Charter> CharterOptions { get; set; } = new();
        public Guid? SelectedCharterId { get; set; }
        public List<string> Providers { get; set; } = new();
        public string? SelectedProvider { get; set; }
        public List<string> Models { get; set; } = new();
        public string? ModelName { get; set; }
        public string? Prompt { get; set; }
        public string? Output { get; set; }
        // Translation fields
        public string? TranslateText { get; set; }
        public string? TranslateTarget { get; set; }
        public string? TranslateOutput { get; set; }
        // Optional context for reply simulation
        public Guid? ThreadId { get; set; }
        public int ContextMessageLimit { get; set; } = 5;
        public string? ReplySuggestion { get; set; }
        // Embedding test fields
        public string? EmbedText { get; set; }
        public int? EmbedDimensions { get; set; }
        public string? EmbedPreview { get; set; }
        public string? CurrentEmbeddingProvider { get; set; }
        public string? CurrentEmbeddingModel { get; set; }
        public string? Error { get; set; }
    }

    [HttpGet]
    [Route("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var providers = chatProviders.Select(p => p.Name).ToList();
        // Prefer saved settings
        var selectedProvider = aiSettings.GenerationProvider;
        if (string.IsNullOrWhiteSpace(selectedProvider)) selectedProvider = providers.FirstOrDefault();
        var models = new List<string>();
        if (!string.IsNullOrWhiteSpace(selectedProvider))
        {
            var prov = chatProviders.First(p => string.Equals(p.Name, selectedProvider, StringComparison.OrdinalIgnoreCase));
            var list = await prov.ListModelsAsync(ct);
            models = list.ToList();
        }
        var vm = new IndexVm
            {
                CharterOptions = await charters.ListAsync(ct),
                Providers = providers,
                SelectedProvider = selectedProvider,
                ModelName = aiSettings.GenerationModel,
                Models = models,
                CurrentEmbeddingProvider = aiSettings.EmbeddingProvider,
                CurrentEmbeddingModel = aiSettings.EmbeddingModel
            };
        ViewData["Title"] = "Admin • AI Test";
        return View(vm);
    }

    [HttpGet]
    [Route("Models")]
    public async Task<IActionResult> Models([FromQuery] string provider, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(provider)) return BadRequest("provider is required");
        var prov = chatProviders.FirstOrDefault(p => string.Equals(p.Name, provider, StringComparison.OrdinalIgnoreCase));
        if (prov == null) return NotFound();
        var models = await prov.ListModelsAsync(ct);
        return Ok(models);
    }

    public record RunRequest(Guid? CharterId, string? Prompt, string? ModelName, string? ProviderName);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Run")]
    public async Task<IActionResult> Run([FromForm] RunRequest req, CancellationToken ct)
    {
        var providers = chatProviders.Select(p => p.Name).ToList();
        var selectedProvider = string.IsNullOrWhiteSpace(req.ProviderName) ? (aiSettings.GenerationProvider ?? providers.FirstOrDefault()) : req.ProviderName;
        var prov = !string.IsNullOrWhiteSpace(selectedProvider)
            ? chatProviders.FirstOrDefault(p => string.Equals(p.Name, selectedProvider, StringComparison.OrdinalIgnoreCase))
            : null;
        var models = new List<string>();
        if (prov != null)
        {
            models = (await prov.ListModelsAsync(ct)).ToList();
        }
        var vm = new IndexVm
        {
            CharterOptions = await charters.ListAsync(ct),
            SelectedCharterId = req.CharterId,
            Prompt = req.Prompt,
            ModelName = string.IsNullOrWhiteSpace(req.ModelName) ? aiSettings.GenerationModel : req.ModelName,
            Providers = providers,
            SelectedProvider = selectedProvider,
            Models = models,
            CurrentEmbeddingProvider = aiSettings.EmbeddingProvider,
            CurrentEmbeddingModel = aiSettings.EmbeddingModel
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

        if (prov == null)
        {
            vm.Error = "Selected provider not found.";
            Response.StatusCode = 400;
            return View("Index", vm);
        }

        vm.Output = await prov.GenerateAsync(charter!, req.Prompt!, req.ModelName, null, null, ct);
        return View("Index", vm);
    }

    public record ReplyRequest(Guid? ThreadId, int? ContextMessageLimit, string? Prompt, Guid? CharterId);

    public record TranslateRequest(string? Text, string? TargetLanguage, string? ModelName, string? ProviderName);

    public record EmbedRequest(string? Text);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Translate")]
    public async Task<IActionResult> Translate([FromForm] TranslateRequest req, CancellationToken ct)
    {
        var providers = chatProviders.Select(p => p.Name).ToList();
        var selectedProvider = string.IsNullOrWhiteSpace(req.ProviderName) ? (aiSettings.TranslationProvider ?? providers.FirstOrDefault()) : req.ProviderName;
        var prov = !string.IsNullOrWhiteSpace(selectedProvider)
            ? chatProviders.FirstOrDefault(p => string.Equals(p.Name, selectedProvider, StringComparison.OrdinalIgnoreCase))
            : null;
        var models = new List<string>();
        if (prov != null)
        {
            models = (await prov.ListModelsAsync(ct)).ToList();
        }
        var vm = new IndexVm
        {
            CharterOptions = await charters.ListAsync(ct),
            Providers = providers,
            SelectedProvider = selectedProvider,
            Models = models,
            TranslateText = req.Text,
            TranslateTarget = req.TargetLanguage,
            ModelName = string.IsNullOrWhiteSpace(req.ModelName) ? aiSettings.TranslationModel : req.ModelName,
            CurrentEmbeddingProvider = aiSettings.EmbeddingProvider,
            CurrentEmbeddingModel = aiSettings.EmbeddingModel
        };

        if (string.IsNullOrWhiteSpace(req.Text))
        {
            vm.Error = "Please enter text to translate.";
            Response.StatusCode = 400;
            return View("Index", vm);
        }
        if (string.IsNullOrWhiteSpace(req.TargetLanguage))
        {
            vm.Error = "Please specify a target language (e.g., 'Spanish').";
            Response.StatusCode = 400;
            return View("Index", vm);
        }
        if (prov == null)
        {
            vm.Error = "Selected provider not found.";
            Response.StatusCode = 400;
            return View("Index", vm);
        }

        vm.TranslateOutput = await prov.TranslateAsync(req.Text!, req.TargetLanguage!, req.ModelName, null, null, ct);
        return View("Index", vm);
    }

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

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Embed")]
    public async Task<IActionResult> Embed([FromForm] EmbedRequest req, CancellationToken ct)
    {
        var vm = new IndexVm
        {
            CharterOptions = await charters.ListAsync(ct),
            EmbedText = req.Text,
            CurrentEmbeddingProvider = aiSettings.EmbeddingProvider,
            CurrentEmbeddingModel = aiSettings.EmbeddingModel
        };
        if (string.IsNullOrWhiteSpace(req.Text))
        {
            vm.Error = "Please enter text to embed.";
            Response.StatusCode = 400;
            return View("Index", vm);
        }
        try
        {
            var vec = await embeddings.EmbedAsync(req.Text!, ct);
            vm.EmbedDimensions = vec?.Length;
            if (vec != null && vec.Length > 0)
            {
                var take = Math.Min(12, vec.Length);
                vm.EmbedPreview = string.Join(", ", vec.Take(take).Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture))) + (vec.Length > take ? ", ..." : "");
            }
        }
        catch (Exception ex)
        {
            vm.Error = $"Embedding failed: {ex.Message}";
            Response.StatusCode = 500;
        }
        return View("Index", vm);
    }
}