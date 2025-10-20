using LucidForums.Models.ViewModels;
using LucidForums.Services.Forum;
using Mapster;
using Microsoft.AspNetCore.Mvc;

namespace LucidForums.Controllers;

public class ThreadsController(IThreadViewService threadViewService, IMessageService messageService, LucidForums.Web.Mapping.IAppMapper mapper) : Controller
{
    [HttpGet]
    [Route("Threads/{id:guid}")]
    public async Task<IActionResult> Show(Guid id, CancellationToken ct)
    {
        var view = await threadViewService.GetViewAsync(id, ct);
        if (view == null) return NotFound();
        var vm = mapper.ToThreadVm(view);
        return View(vm);
    }

    [HttpGet]
    [Route("Threads/{id:guid}/Reply")]
    public IActionResult Reply(Guid id, Guid? parentId)
    {
        var vm = new ReplyVm { ThreadId = id, ParentId = parentId };
        return PartialView("_ReplyForm", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Threads/{id:guid}/Reply")] 
    public async Task<IActionResult> Reply(Guid id, ReplyVm vm, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = 400;
            return PartialView("_ReplyForm", vm);
        }
        var msg = await messageService.ReplyAsync(id, vm.ParentId, vm.Content, User?.Identity?.Name, ct);
        var messageDto = new LucidForums.Models.Dtos.MessageView(msg.Id, msg.ParentId, msg.Content, msg.CreatedById, msg.CreatedAtUtc, 0);
        var messageVm = mapper.ToMessageVm(messageDto);
        // For now depth is unknown; client may insert appropriately. Optionally fetch thread view slice.
        return PartialView("_Message", messageVm);
    }
}
