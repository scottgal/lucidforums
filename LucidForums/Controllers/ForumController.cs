using LucidForums.Models.ViewModels;
using LucidForums.Services.Forum;
using Mapster;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

public class ForumController(IForumService forumService, IThreadService threadService, LucidForums.Web.Mapping.IAppMapper mapper) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var forums = await forumService.ListAsync(ct: ct);
        var vms = mapper.ToForumListItemVms(forums);
        return View(vms);
    }

    [HttpGet]
    [Route("Forum/{slug}")]
    public async Task<IActionResult> Details(string slug, CancellationToken ct)
    {
        var forum = await forumService.GetBySlugAsync(slug, ct);
        if (forum == null) return NotFound();
        var threads = await threadService.ListByForumAsync(forum.Id, 0, 50, ct);
        var threadVms = mapper.ToThreadSummaryVms(threads).ToList();
        var vm = new ForumDetailsVm(forum.Id, forum.Slug, forum.Name, forum.Description, threadVms);
        if (Request.Headers.TryGetValue("HX-Request", out var hx) && hx == "true")
            return PartialView("_ThreadList", threadVms);
        return View(vm);
    }

    [HttpGet]
    [Route("Forum/{forumId:guid}/CreateThread")]
    public IActionResult CreateThread(Guid forumId)
    {
        return PartialView("_CreateThreadForm", new CreateThreadVm { ForumId = forumId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Forum/{forumId:guid}/CreateThread")]
    public async Task<IActionResult> CreateThread(Guid forumId, CreateThreadVm vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = 400;
            return PartialView("_CreateThreadForm", vm);
        }
        var title = vm.Title;
        var content = vm.Content;
        var thread = await threadService.CreateAsync(forumId, title, content, User?.Identity?.Name, ct);
        // Return the thread summary partial for list insertion
        var summary = mapper.ToThreadSummaryVm(thread);
        return PartialView("_ThreadListItem", summary);
    }
}