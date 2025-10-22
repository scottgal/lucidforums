using LucidForums.Helpers;
using LucidForums.Models.ViewModels;
using LucidForums.Services.Forum;
using LucidForums.Services.Translation;
using Mapster;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using LucidForums.Hubs;

namespace LucidForums.Controllers;

public class ThreadsController(
    IThreadViewService threadViewService,
    IMessageService messageService,
    IContentTranslationService contentTranslationService,
    TranslationHelper translationHelper,
    LucidForums.Web.Mapping.IAppMapper mapper,
    IHubContext<ForumHub> hubContext) : Controller
{
    [HttpGet]
    [Route("Threads/{id:guid}")]
    public async Task<IActionResult> Show(Guid id, CancellationToken ct)
    {
        var view = await threadViewService.GetViewAsync(id, ct);
        if (view == null) return NotFound();
        var vm = mapper.ToThreadVm(view);

        // Translate message content if user is viewing in non-English language
        var language = translationHelper.GetCurrentLanguage();
        if (language != "en" && vm.Messages != null && vm.Messages.Count > 0)
        {
            var translatedMessages = new List<MessageVm>();
            foreach (var message in vm.Messages)
            {
                var translation = await contentTranslationService.GetTranslationAsync(
                    "Message",
                    message.Id.ToString(),
                    "Content",
                    language,
                    ct);

                var content = !string.IsNullOrEmpty(translation) ? translation : message.Content;
                var translatedMessage = new MessageVm(message.Id, message.ParentId, content, message.AuthorId, message.CreatedAtUtc, message.Depth, message.CharterScore);
                translatedMessages.Add(translatedMessage);
            }

            vm = new ThreadVm(vm.Id, vm.Title, vm.ForumId, vm.AuthorId, vm.CreatedAtUtc, translatedMessages, vm.CharterScore, vm.Tags);
        }

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
        var messageDto = new LucidForums.Models.Dtos.MessageView(msg.Id, msg.ParentId, msg.Content, msg.CreatedById, msg.CreatedAtUtc, 0, msg.CharterScore);
        var messageVm = mapper.ToMessageVm(messageDto);

        // Notify listeners in this thread about the new message
        await hubContext.Clients.Group(ForumHub.GroupName(id.ToString()))
            .SendAsync("NewMessage", id.ToString(), msg.Id.ToString(), ct);

        // For now depth is unknown; client may insert appropriately. Optionally fetch thread view slice.
        return PartialView("_Message", messageVm);
    }
    [HttpGet]
    [Route("Threads/Message/{id:guid}")]
    public async Task<IActionResult> Message(Guid id, CancellationToken ct)
    {
        var msg = await messageService.GetAsync(id, ct);
        if (msg == null) return NotFound();
        var depth = string.IsNullOrEmpty(msg.Path) ? 0 : msg.Path.Split('.').Length - 1;

        var content = msg.Content;
        // Translate if user is viewing in non-English language
        var language = translationHelper.GetCurrentLanguage();
        if (language != "en")
        {
            var translation = await contentTranslationService.GetTranslationAsync(
                "Message",
                msg.Id.ToString(),
                "Content",
                language,
                ct);

            if (!string.IsNullOrEmpty(translation))
            {
                content = translation;
            }
        }

        var messageDto = new LucidForums.Models.Dtos.MessageView(msg.Id, msg.ParentId, content, msg.CreatedById, msg.CreatedAtUtc, depth, msg.CharterScore);
        var messageVm = mapper.ToMessageVm(messageDto);
        return PartialView("_Message", messageVm);
    }
}
