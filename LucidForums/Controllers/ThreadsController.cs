using LucidForums.Helpers;
using LucidForums.Models.ViewModels;
using LucidForums.Services.Forum;
using LucidForums.Services.Translation;
using LucidForums.Services.Ai;
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
    IHubContext<ForumHub> hubContext,
    ITextAiService textAiService) : Controller
{
    [HttpGet]
    [Route("Threads/{id:guid}")]
    public async Task<IActionResult> Show(Guid id, CancellationToken ct)
    {
        var view = await threadViewService.GetViewAsync(id, ct);
        if (view == null) return NotFound();
        var vm = mapper.ToThreadVm(view);

        // Auto-translate message content if user is viewing in non-English language; also include original
        var language = translationHelper.GetCurrentLanguage();

        // Trigger translation for thread title so it updates via SignalR when ready
        if (!string.IsNullOrWhiteSpace(language) && !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            var existingThreadTitle = await contentTranslationService.GetTranslationAsync(
                "Thread",
                vm.Id.ToString(),
                "Title",
                language,
                ct);
            if (string.IsNullOrWhiteSpace(existingThreadTitle))
            {
                _ = contentTranslationService.TranslateContentAsync(
                    "Thread",
                    vm.Id.ToString(),
                    "Title",
                    vm.Title,
                    language,
                    ct);
            }
        }

        if (vm.Messages != null && vm.Messages.Count > 0)
        {
            var translatedMessages = new List<MessageVm>(vm.Messages.Count);
            foreach (var message in vm.Messages)
            {
                string? translated = null;
                if (!string.IsNullOrWhiteSpace(language) && !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
                {
                    var existing = await contentTranslationService.GetTranslationAsync(
                        "Message",
                        message.Id.ToString(),
                        "Content",
                        language,
                        ct);
                    if (string.IsNullOrWhiteSpace(existing))
                    {
                        // Trigger translation generation and broadcast via SignalR; UI will update when ready
                        _ = contentTranslationService.TranslateContentAsync(
                            "Message",
                            message.Id.ToString(),
                            "Content",
                            message.Content,
                            language,
                            ct);
                    }
                }
                var translatedMessage = new MessageVm(
                    message.Id,
                    message.ParentId,
                    message.Content,
                    message.AuthorId,
                    message.CreatedAtUtc,
                    message.Depth,
                    message.CharterScore,
                    translated,
                    null,
                    language);
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

        // Prepare view model with original and translated content for current language
        var language = translationHelper.GetCurrentLanguage();
        string? translated = null;
        if (!string.IsNullOrWhiteSpace(language) && !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            var existing = await contentTranslationService.GetTranslationAsync(
                "Message",
                msg.Id.ToString(),
                "Content",
                language,
                ct);
            if (string.IsNullOrWhiteSpace(existing))
            {
                // Trigger translation but don't inline the result; clients will update via SignalR
                _ = contentTranslationService.TranslateContentAsync(
                    "Message",
                    msg.Id.ToString(),
                    "Content",
                    msg.Content,
                    language,
                    ct);
            }
        }
        var messageVm = new MessageVm(msg.Id, msg.ParentId, msg.Content, msg.CreatedById, msg.CreatedAtUtc, 0, msg.CharterScore, translated, null, language);

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

        var language = translationHelper.GetCurrentLanguage();
        string? translated = null;
        if (!string.IsNullOrWhiteSpace(language) && !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            translated = await contentTranslationService.GetTranslationAsync(
                "Message",
                msg.Id.ToString(),
                "Content",
                language,
                ct);
            if (string.IsNullOrWhiteSpace(translated))
            {
                translated = await contentTranslationService.TranslateContentAsync(
                    "Message",
                    msg.Id.ToString(),
                    "Content",
                    msg.Content,
                    language,
                    ct);
            }
        }

        var messageVm = new MessageVm(msg.Id, msg.ParentId, msg.Content, msg.CreatedById, msg.CreatedAtUtc, depth, msg.CharterScore, translated, null, language);
        return PartialView("_Message", messageVm);
    }
}
