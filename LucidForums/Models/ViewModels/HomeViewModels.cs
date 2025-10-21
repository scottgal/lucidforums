namespace LucidForums.Models.ViewModels;

public record HomeIndexVm(
    IReadOnlyList<ForumListItemVm> Forums,
    IReadOnlyList<ThreadSummaryVm> RecentThreads
);
