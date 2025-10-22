using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using LucidForums.Models;
using LucidForums.Extensions;

namespace LucidForums.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly Services.Forum.IForumService _forumService;
    private readonly Services.Forum.IThreadService _threadService;
    private readonly LucidForums.Web.Mapping.IAppMapper _mapper;

    public HomeController(ILogger<HomeController> logger,
        Services.Forum.IForumService forumService,
        Services.Forum.IThreadService threadService,
        LucidForums.Web.Mapping.IAppMapper mapper)
    {
        _logger = logger;
        _forumService = forumService;
        _threadService = threadService;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var forums = await _forumService.ListAsync(ct: ct);
        var latestThreads = await _threadService.ListLatestAsync(take: 10, ct: ct);
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
        var forumVms = _mapper.ToForumListItemVms(filtered).ToList();
        return PartialView("_ForumCards", forumVms);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}