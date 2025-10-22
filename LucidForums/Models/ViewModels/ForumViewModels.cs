namespace LucidForums.Models.ViewModels;

public record ForumListItemVm(Guid Id, string Slug, string Name, string? Description);

public record ThreadSummaryVm(Guid Id, Guid ForumId, string Title, string? AuthorId, DateTime CreatedAtUtc, double? CharterScore, int ReplyCount, DateTime LastInteractionUtc);

public record ForumDetailsVm(Guid Id, string Slug, string Name, string? Description, int ThreadCount, Guid? CharterId, string? CharterName, string? CharterPurpose, IReadOnlyList<ThreadSummaryVm> Threads);
