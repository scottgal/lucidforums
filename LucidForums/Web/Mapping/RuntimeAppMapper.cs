using System.Linq;
using LucidForums.Models.Dtos;
using LucidForums.Models.Entities;
using LucidForums.Models.ViewModels;
using Mapster;

namespace LucidForums.Web.Mapping;

// Runtime fallback implementation used when Mapster source-generated mapper is unavailable.
// This relies on Mapster's TypeAdapter configuration set up in MapsterRegistration.
public class RuntimeAppMapper : IAppMapper
{
    public ThreadVm ToThreadVm(ThreadView src)
    {
        // Map top-level properties and adapt messages
        var messages = (src.Messages ?? Array.Empty<MessageView>())
            .Select(ToMessageVm)
            .ToList();
        return new ThreadVm(src.Id, src.Title, src.ForumId, src.AuthorId, src.CreatedAtUtc, messages, src.CharterScore, src.Tags ?? Array.Empty<string>());
    }

    public MessageVm ToMessageVm(MessageView src)
    {
        return new MessageVm(
            src.Id,
            src.ParentId,
            src.Content,
            src.AuthorId,
            src.CreatedAtUtc,
            src.Depth,
            src.CharterScore,
            null,
            null,
            null);
    }

    public ThreadSummaryVm ToThreadSummaryVm(ForumThread src)
    {
        // Keep parity with MapsterRegistration mapping; note ReplyCount/LastInteraction not available here
        return new ThreadSummaryVm(src.Id, src.ForumId, src.Title, src.CreatedById, src.CreatedAtUtc, src.CharterScore, 0, src.CreatedAtUtc);
    }

    public IEnumerable<ThreadSummaryVm> ToThreadSummaryVms(IEnumerable<ForumThread> src)
    {
        return (src ?? Enumerable.Empty<ForumThread>()).Select(ToThreadSummaryVm).ToList();
    }

    public IEnumerable<ForumListItemVm> ToForumListItemVms(IEnumerable<Forum> src)
    {
        // Use TypeAdapter for simple projection configured in MapsterRegistration
        return (src ?? Enumerable.Empty<Forum>()).Select(s => s.Adapt<ForumListItemVm>()).ToList();
    }

    public CharterListItemVm ToCharterListItemVm(Charter src)
    {
        return src.Adapt<CharterListItemVm>();
    }

    public IEnumerable<CharterListItemVm> ToCharterListItemVms(IEnumerable<Charter> src)
    {
        return (src ?? Enumerable.Empty<Charter>()).Select(ToCharterListItemVm).ToList();
    }

    public CharterDetailsVm ToCharterDetailsVm(Charter src)
    {
        // Ensure rules/behaviors are non-null lists
        var vm = src.Adapt<CharterDetailsVm>();
        var rules = (src.Rules ?? new List<string>()).ToList();
        var behaviors = (src.Behaviors ?? new List<string>()).ToList();
        return new CharterDetailsVm(src.Id, src.Name, src.Purpose, rules, behaviors);
    }
}
