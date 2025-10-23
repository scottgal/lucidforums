using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using LucidForums.Models;
using LucidForums.Extensions;
using LucidForums.Services.Translation;
using LucidForums.Helpers;

namespace LucidForums.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly Services.Forum.IForumService _forumService;
    private readonly Services.Forum.IThreadService _threadService;
    private readonly LucidForums.Web.Mapping.IAppMapper _mapper;
    private readonly IContentTranslationService _contentTranslationService;
    private readonly TranslationHelper _translationHelper;

    public HomeController(ILogger<HomeController> logger,
        Services.Forum.IForumService forumService,
        Services.Forum.IThreadService threadService,
        LucidForums.Web.Mapping.IAppMapper mapper,
        IContentTranslationService contentTranslationService,
        TranslationHelper translationHelper)
    {
        _logger = logger;
        _forumService = forumService;
        _threadService = threadService;
        _mapper = mapper;
        _contentTranslationService = contentTranslationService;
        _translationHelper = translationHelper;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var forums = await _forumService.ListAsync(ct: ct);
        var latestThreads = await _threadService.ListLatestAsync(take: 10, ct: ct);

        // Trigger forum title/description translations for current language (fire-and-forget)
        var language = _translationHelper.GetCurrentLanguage();
        if (!string.IsNullOrWhiteSpace(language) && !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var f in forums ?? Enumerable.Empty<LucidForums.Models.Entities.Forum>())
            {
                try
                {
                    var id = f.Id.ToString();
                    // Name
                    var existingName = await _contentTranslationService.GetTranslationAsync("Forum", id, "Name", language, ct);
                    if (string.IsNullOrWhiteSpace(existingName))
                    {
                        _ = _contentTranslationService.TranslateContentAsync("Forum", id, "Name", f.Name, language, ct);
                    }
                    // Description (if any)
                    if (!string.IsNullOrWhiteSpace(f.Description))
                    {
                        var existingDesc = await _contentTranslationService.GetTranslationAsync("Forum", id, "Description", language, ct);
                        if (string.IsNullOrWhiteSpace(existingDesc))
                        {
                            _ = _contentTranslationService.TranslateContentAsync("Forum", id, "Description", f.Description!, language, ct);
                        }
                    }
                }
                catch
                {
                    // Best-effort; do not block page load
                }
            }
        }

        var forumVms = _mapper.ToForumListItemVms(forums).ToList();
        var threadVms = _mapper.ToThreadSummaryVms(latestThreads).ToList();
        var vm = new Models.ViewModels.HomeIndexVm(forumVms, threadVms);
        return View(vm);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> SearchForums(string? q, CancellationToken ct)
    {
        var forums = await _forumService.ListAsync(ct: ct);
        IEnumerable<LucidForums.Models.Entities.Forum> filtered = forums ?? Enumerable.Empty<LucidForums.Models.Entities.Forum>();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            filtered = filtered.Where(f => f.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                                     || (f.Description != null && f.Description.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        // Trigger translations for the filtered list as well
        var language = _translationHelper.GetCurrentLanguage();
        if (!string.IsNullOrWhiteSpace(language) && !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var f in filtered)
            {
                try
                {
                    var id = f.Id.ToString();
                    var existingName = await _contentTranslationService.GetTranslationAsync("Forum", id, "Name", language, ct);
                    if (string.IsNullOrWhiteSpace(existingName))
                    {
                        _ = _contentTranslationService.TranslateContentAsync("Forum", id, "Name", f.Name, language, ct);
                    }
                    if (!string.IsNullOrWhiteSpace(f.Description))
                    {
                        var existingDesc = await _contentTranslationService.GetTranslationAsync("Forum", id, "Description", language, ct);
                        if (string.IsNullOrWhiteSpace(existingDesc))
                        {
                            _ = _contentTranslationService.TranslateContentAsync("Forum", id, "Description", f.Description!, language, ct);
                        }
                    }
                }
                catch { }
            }
        }

        var forumVms = _mapper.ToForumListItemVms(filtered).ToList();
        return PartialView("_ForumCards", forumVms);
    }

    [HttpGet]
    public async Task<IActionResult> RecentThreads(CancellationToken ct)
    {
        var latestThreads = await _threadService.ListLatestAsync(take: 10, ct: ct);

        // Trigger thread title translations for current language (fire-and-forget)
        var language = _translationHelper.GetCurrentLanguage();
        if (!string.IsNullOrWhiteSpace(language) && !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var thread in latestThreads ?? Enumerable.Empty<LucidForums.Models.Entities.ForumThread>())
            {
                try
                {
                    var id = thread.Id.ToString();
                    var existingTitle = await _contentTranslationService.GetTranslationAsync("Thread", id, "Title", language, ct);
                    if (string.IsNullOrWhiteSpace(existingTitle))
                    {
                        _ = _contentTranslationService.TranslateContentAsync("Thread", id, "Title", thread.Title, language, ct);
                    }
                }
                catch
                {
                    // Best-effort; do not block response
                }
            }
        }

        var threadVms = _mapper.ToThreadSummaryVms(latestThreads).ToList();
        return PartialView("_RecentThreads", threadVms);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}