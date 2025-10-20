namespace LucidForums.Models.ViewModels;

public record ForumListItemVm(Guid Id, string Slug, string Name, string? Description);

public record ThreadSummaryVm(Guid Id, Guid ForumId, string Title, string? AuthorId, DateTime CreatedAtUtc);

public record ForumDetailsVm(Guid Id, string Slug, string Name, string? Description, IReadOnlyList<ThreadSummaryVm> Threads);
