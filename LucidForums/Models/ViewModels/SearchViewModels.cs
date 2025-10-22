namespace LucidForums.Models.ViewModels;

public record SearchResultVm(
    string ForumSlug,
    Guid ForumId,
    Guid ThreadId,
    string ThreadTitle,
    Guid MessageId,
    string Snippet,
    double Score,
    double? SemanticScore,
    double? FullTextScore,
    string AuthorName,
    DateTime CreatedAt
);

public record ForumOptionVm(Guid ForumId, string ForumName, string ForumSlug);

public record SearchPageVm(
    string Query,
    Guid? ForumId,
    bool MyPosts,
    string SearchMode,
    DateTime? StartDate,
    DateTime? EndDate,
    List<SearchResultVm> Results,
    List<ForumOptionVm> AvailableForums
);
