using System.Linq;
using LucidForums.Services.Ai;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

[Route("Admin/AiSettings")] 
public class AdminAiSettingsController(IAiSettingsService settings, IEnumerable<IChatProvider> providers) : Controller
{
    public class IndexVm
    {
        public List<string> Providers { get; set; } = new();
        // Generation
        public string? GenerationProvider { get; set; }
        public List<string> GenerationModels { get; set; } = new();
        public string? GenerationModel { get; set; }
        // Translation
        public string? TranslationProvider { get; set; }
        public List<string> TranslationModels { get; set; } = new();
        public string? TranslationModel { get; set; }
        // Embedding
        public string? EmbeddingProvider { get; set; }
        public List<string> EmbeddingModels { get; set; } = new();
        public string? EmbeddingModel { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
    }

    [HttpGet]
    [Route("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var provNames = providers.Select(p => p.Name).ToList();
        settings.SetKnownProviders(provNames);

        async Task<List<string>> Load(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return new();
            var pr = providers.FirstOrDefault(x => string.Equals(x.Name, p, StringComparison.OrdinalIgnoreCase));
            if (pr == null) return new();
            var models = await pr.ListModelsAsync(ct);
            return models.ToList();
        }

        var vm = new IndexVm
        {
            Providers = provNames,
            GenerationProvider = settings.GenerationProvider ?? provNames.FirstOrDefault(),
            TranslationProvider = settings.TranslationProvider ?? provNames.FirstOrDefault(),
            EmbeddingProvider = settings.EmbeddingProvider ?? provNames.FirstOrDefault(),
            GenerationModel = settings.GenerationModel,
            TranslationModel = settings.TranslationModel,
            EmbeddingModel = settings.EmbeddingModel
        };
        vm.GenerationModels = await Load(vm.GenerationProvider);
        vm.TranslationModels = await Load(vm.TranslationProvider);
        vm.EmbeddingModels = await Load(vm.EmbeddingProvider);
        ViewData["Title"] = "Admin • AI Settings";
        return View(vm);
    }

    public record SaveRequest(
        string? GenerationProvider, string? GenerationModel,
        string? TranslationProvider, string? TranslationModel,
        string? EmbeddingProvider, string? EmbeddingModel
    );

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Save")]
    public async Task<IActionResult> Save([FromForm] SaveRequest req, CancellationToken ct)
    {
        var provNames = providers.Select(p => p.Name).ToList();
        settings.SetKnownProviders(provNames);
        if (!string.IsNullOrWhiteSpace(req.GenerationProvider) && provNames.Contains(req.GenerationProvider)) settings.GenerationProvider = req.GenerationProvider;
        if (!string.IsNullOrWhiteSpace(req.TranslationProvider) && provNames.Contains(req.TranslationProvider)) settings.TranslationProvider = req.TranslationProvider;
        if (!string.IsNullOrWhiteSpace(req.EmbeddingProvider) && provNames.Contains(req.EmbeddingProvider)) settings.EmbeddingProvider = req.EmbeddingProvider;
        settings.GenerationModel = string.IsNullOrWhiteSpace(req.GenerationModel) ? null : req.GenerationModel;
        settings.TranslationModel = string.IsNullOrWhiteSpace(req.TranslationModel) ? null : req.TranslationModel;
        settings.EmbeddingModel = string.IsNullOrWhiteSpace(req.EmbeddingModel) ? null : req.EmbeddingModel;

        // Reload models and show message
        return await Index(ct).ContinueWith<IActionResult>(t =>
        {
            if (t.Result is ViewResult vr && vr.Model is IndexVm vm)
            {
                vm.Message = "Settings saved.";
                return View("Index", vm);
            }
            return RedirectToAction("Index");
        });
    }

    [HttpGet]
    [Route("Models")] 
    public async Task<IActionResult> Models([FromQuery] string provider, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(provider)) return BadRequest("provider is required");
        var pr = providers.FirstOrDefault(p => string.Equals(p.Name, provider, StringComparison.OrdinalIgnoreCase));
        if (pr == null) return NotFound();
        var models = await pr.ListModelsAsync(ct);
        return Ok(models);
    }
}