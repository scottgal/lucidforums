using LucidForums.Data;
using LucidForums.Extensions;
using LucidForums.Hubs;
using LucidForums.Services.Translation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Controllers;

[Authorize(Roles = "Administrator")]
[Route("Admin/Translation")]
public class AdminTranslationController : Controller
{
    private readonly ITranslationService _translationService;
    private readonly IContentTranslationService _contentTranslationService;
    private readonly ApplicationDbContext _db;
    private readonly IHubContext<TranslationHub> _hubContext;
    private readonly ILogger<AdminTranslationController> _logger;

    public AdminTranslationController(
        ITranslationService translationService,
        IContentTranslationService contentTranslationService,
        ApplicationDbContext db,
        IHubContext<TranslationHub> hubContext,
        ILogger<AdminTranslationController> logger)
    {
        _translationService = translationService;
        _contentTranslationService = contentTranslationService;
        _db = db;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var languages = await _translationService.GetAvailableLanguagesAsync(ct);
        var stats = new List<TranslationStats>();

        foreach (var lang in languages)
        {
            var langStats = await _translationService.GetStatsAsync(lang, ct);
            stats.Add(langStats);
        }

        var vm = new IndexVm
        {
            AvailableLanguages = languages,
            Stats = stats,
            TotalStrings = await _db.TranslationStrings.CountAsync(ct)
        };

        return View(vm);
    }

    [HttpPost("TranslateStrings")]
    public async Task<IActionResult> TranslateStrings([FromForm] string targetLanguage, CancellationToken ct)
    {
        var jobId = Guid.NewGuid().ToString();

        // Start translation in background
        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<TranslationProgress>(p =>
                {
                    _hubContext.Clients.All.SendAsync("TranslationProgress", new
                    {
                        JobId = jobId,
                        Total = p.Total,
                        Completed = p.Completed,
                        CurrentKey = p.CurrentKey,
                        Percentage = p.Total > 0 ? (p.Completed / (double)p.Total) * 100.0 : 0
                    }, ct);
                });

                var count = await _translationService.TranslateAllStringsAsync(targetLanguage, false, progress, ct);

                await _hubContext.Clients.All.SendAsync("TranslationComplete", new
                {
                    JobId = jobId,
                    TranslatedCount = count
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to translate strings to {Language}", targetLanguage);
            }
        }, CancellationToken.None);

        if (Request.IsHtmxRequest())
        {
            return Content($@"<div class=""alert alert-info"">
                <span>Translation started for <strong>{targetLanguage}</strong>. Progress will update below.</span>
            </div>
            <div id=""job-{jobId}"" class=""mt-4"">
                <progress class=""progress progress-primary w-full"" value=""0"" max=""100""></progress>
                <div class=""text-sm mt-2"">Translating...</div>
            </div>", "text/html");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("TranslateContent")]
    public async Task<IActionResult> TranslateContent([FromForm] string contentType, [FromForm] string targetLanguage, CancellationToken ct)
    {
        var jobId = Guid.NewGuid().ToString();

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<TranslationProgress>(p =>
                {
                    _hubContext.Clients.All.SendAsync("TranslationProgress", new
                    {
                        JobId = jobId,
                        Total = p.Total,
                        Completed = p.Completed,
                        CurrentKey = p.CurrentKey,
                        Percentage = p.Total > 0 ? (p.Completed / (double)p.Total) * 100.0 : 0
                    }, ct);
                });

                var count = await _contentTranslationService.TranslateAllContentAsync(contentType, targetLanguage, progress, ct);

                await _hubContext.Clients.All.SendAsync("TranslationComplete", new
                {
                    JobId = jobId,
                    TranslatedCount = count
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to translate {ContentType} to {Language}", contentType, targetLanguage);
            }
        }, CancellationToken.None);

        if (Request.IsHtmxRequest())
        {
            return Content($@"<div class=""alert alert-info"">
                <span>Content translation started for <strong>{contentType}</strong> to <strong>{targetLanguage}</strong>.</span>
            </div>
            <div id=""job-{jobId}"" class=""mt-4"">
                <progress class=""progress progress-primary w-full"" value=""0"" max=""100""></progress>
                <div class=""text-sm mt-2"">Translating content...</div>
            </div>", "text/html");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Strings")]
    public async Task<IActionResult> Strings([FromQuery] string? category, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var pageSize = 50;
        var query = _db.TranslationStrings.AsQueryable();

        if (!string.IsNullOrEmpty(category))
            query = query.Where(ts => ts.Category == category);

        var total = await query.CountAsync(ct);
        var strings = await query
            .OrderBy(ts => ts.Category)
            .ThenBy(ts => ts.Key)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var vm = new StringsVm
        {
            Strings = strings,
            Category = category,
            Page = page,
            PageSize = pageSize,
            Total = total
        };

        return View(vm);
    }

    public record IndexVm
    {
        public List<string> AvailableLanguages { get; set; } = new();
        public List<TranslationStats> Stats { get; set; } = new();
        public int TotalStrings { get; set; }
    }

    public record StringsVm
    {
        public List<Models.Entities.TranslationString> Strings { get; set; } = new();
        public string? Category { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public int TotalPages => (int)Math.Ceiling(Total / (double)PageSize);
    }
}
