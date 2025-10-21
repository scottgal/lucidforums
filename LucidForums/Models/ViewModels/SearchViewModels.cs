namespace LucidForums.Models.ViewModels;

public record SearchResultVm(string? ForumSlug, Guid ForumId, Guid ThreadId, Guid MessageId, string Snippet, double Score);

public record SearchPageVm(string? ForumSlug, string Query, List<SearchResultVm> Results);
