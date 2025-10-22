namespace LucidForums.Services.Search;

public enum SearchMode
{
    Hybrid,      // Combine full-text and semantic search (default)
    FullText,    // Full-text search only
    Semantic     // Semantic/vector search only
}

public record SearchResult(
    Guid MessageId,
    Guid ThreadId,
    Guid ForumId,
    string ForumSlug,
    string ThreadTitle,
    string MessageContent,
    string AuthorName,
    DateTime CreatedAt,
    double Score,
    double? SemanticScore,
    double? FullTextScore,
    string Snippet
);

public record SearchOptions(
    string Query,
    Guid? ForumId = null,
    string? UserId = null,  // Search only user's own messages
    SearchMode Mode = SearchMode.Hybrid,
    int Limit = 20,
    int Offset = 0,
    DateTime? StartDate = null,
    DateTime? EndDate = null
);

public interface ISearchService
{
    /// <summary>
    /// Performs a hybrid search combining full-text and semantic search
    /// </summary>
    Task<List<SearchResult>> SearchAsync(SearchOptions options, CancellationToken ct = default);

    /// <summary>
    /// Get available forums for dropdown/filter
    /// </summary>
    Task<List<(Guid ForumId, string ForumName, string ForumSlug)>> GetForumsAsync(CancellationToken ct = default);
}