using LucidForums.Models.ViewModels;
using LucidForums.Services.Search;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LucidForums.Controllers;

public class SearchController(ISearchService searchService) : Controller
{
    [HttpGet]
    [Route("Search")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q,
        [FromQuery] Guid? forumId,
        [FromQuery] bool? myPosts,
        [FromQuery] string? mode,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken ct)
    {
        var query = q ?? string.Empty;

        // Get forums for dropdown
        var forums = await searchService.GetForumsAsync(ct);

        // Parse search mode
        var searchMode = mode?.ToLowerInvariant() switch
        {
            "fulltext" => SearchMode.FullText,
            "semantic" => SearchMode.Semantic,
            _ => SearchMode.Hybrid
        };

        // Get user ID if searching own posts
        string? userId = null;
        if (myPosts == true && User.Identity?.IsAuthenticated == true)
        {
            userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        // Perform search
        List<SearchResult> results = new();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var options = new SearchOptions(
                Query: query,
                ForumId: forumId,
                UserId: userId,
                Mode: searchMode,
                Limit: 50,
                StartDate: startDate,
                EndDate: endDate
            );

            results = await searchService.SearchAsync(options, ct);
        }

        // Map to view model
        var resultVms = results.Select(r => new SearchResultVm(
            ForumSlug: r.ForumSlug,
            ForumId: r.ForumId,
            ThreadId: r.ThreadId,
            ThreadTitle: r.ThreadTitle,
            MessageId: r.MessageId,
            Snippet: r.Snippet,
            Score: r.Score,
            SemanticScore: r.SemanticScore,
            FullTextScore: r.FullTextScore,
            AuthorName: r.AuthorName,
            CreatedAt: r.CreatedAt
        )).ToList();

        var vm = new SearchPageVm(
            Query: query,
            ForumId: forumId,
            MyPosts: myPosts ?? false,
            SearchMode: mode ?? "hybrid",
            StartDate: startDate,
            EndDate: endDate,
            Results: resultVms,
            AvailableForums: forums.Select(f => new ForumOptionVm(f.ForumId, f.ForumName, f.ForumSlug)).ToList()
        );

        return View(vm);
    }
}
