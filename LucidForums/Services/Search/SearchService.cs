using LucidForums.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Search;

public class SearchService(
    ApplicationDbContext db,
    IEmbeddingService embeddingService,
    ILogger<SearchService> logger) : ISearchService
{
    private const double FullTextWeight = 0.4;
    private const double SemanticWeight = 0.6;

    public async Task<List<SearchResult>> SearchAsync(SearchOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Query))
        {
            return new List<SearchResult>();
        }

        var results = options.Mode switch
        {
            SearchMode.FullText => await FullTextSearchAsync(options, ct),
            SearchMode.Semantic => await SemanticSearchAsync(options, ct),
            SearchMode.Hybrid => await HybridSearchAsync(options, ct),
            _ => await HybridSearchAsync(options, ct)
        };

        return results
            .Skip(options.Offset)
            .Take(options.Limit)
            .ToList();
    }

    private async Task<List<SearchResult>> HybridSearchAsync(SearchOptions options, CancellationToken ct)
    {
        // Get results from both search methods
        var fullTextTask = FullTextSearchAsync(options with { Limit = 50 }, ct);
        var semanticTask = SemanticSearchAsync(options with { Limit = 50 }, ct);

        await Task.WhenAll(fullTextTask, semanticTask);

        var fullTextResults = await fullTextTask;
        var semanticResults = await semanticTask;

        // Normalize scores to 0-1 range
        var maxFtScore = fullTextResults.Any() ? fullTextResults.Max(r => r.Score) : 1.0;
        var maxSemScore = semanticResults.Any() ? semanticResults.Max(r => r.Score) : 1.0;

        // Create a dictionary to merge results
        var mergedResults = new Dictionary<Guid, SearchResult>();

        // Add full-text results
        foreach (var result in fullTextResults)
        {
            var normalizedScore = maxFtScore > 0 ? result.Score / maxFtScore : 0;
            mergedResults[result.MessageId] = result with
            {
                Score = normalizedScore * FullTextWeight,
                FullTextScore = normalizedScore
            };
        }

        // Merge semantic results
        foreach (var result in semanticResults)
        {
            // For semantic search, lower distance = better match, so invert it
            var normalizedScore = maxSemScore > 0 ? 1.0 - (result.Score / maxSemScore) : 0;

            if (mergedResults.TryGetValue(result.MessageId, out var existing))
            {
                // Combine scores
                mergedResults[result.MessageId] = existing with
                {
                    Score = existing.Score + (normalizedScore * SemanticWeight),
                    SemanticScore = normalizedScore
                };
            }
            else
            {
                mergedResults[result.MessageId] = result with
                {
                    Score = normalizedScore * SemanticWeight,
                    SemanticScore = normalizedScore
                };
            }
        }

        return mergedResults.Values
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    private async Task<List<SearchResult>> FullTextSearchAsync(SearchOptions options, CancellationToken ct)
    {
        try
        {
            // Build the search query with PostgreSQL full-text search
            var query = db.Messages.AsNoTracking()
                .Include(m => m.Thread)
                    .ThenInclude(t => t.Forum)
                .Include(m => m.CreatedBy)
                .AsQueryable();

            // Apply forum filter
            if (options.ForumId.HasValue)
            {
                query = query.Where(m => m.Thread.ForumId == options.ForumId.Value);
            }

            // Apply user filter
            if (!string.IsNullOrEmpty(options.UserId))
            {
                query = query.Where(m => m.CreatedById == options.UserId);
            }

            // Apply date filters
            if (options.StartDate.HasValue)
            {
                query = query.Where(m => m.CreatedAtUtc >= options.StartDate.Value);
            }

            if (options.EndDate.HasValue)
            {
                query = query.Where(m => m.CreatedAtUtc <= options.EndDate.Value);
            }

            // Use PostgreSQL to_tsvector and ts_rank for full-text search
            var searchQuery = options.Query.Trim();
            var tsQuery = string.Join(" & ", searchQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            var sql = $@"
                SELECT
                    m.""Id"" as message_id,
                    m.""ForumThreadId"" as thread_id,
                    t.""ForumId"" as forum_id,
                    f.""Slug"" as forum_slug,
                    t.""Title"" as thread_title,
                    m.""Content"" as message_content,
                    COALESCE(u.""UserName"", 'Anonymous') as author_name,
                    m.""CreatedAtUtc"" as created_at,
                    ts_rank(to_tsvector('english', m.""Content""), plainto_tsquery('english', {{0}})) as score
                FROM ""Messages"" m
                INNER JOIN ""Threads"" t ON m.""ForumThreadId"" = t.""Id""
                INNER JOIN ""Forums"" f ON t.""ForumId"" = f.""Id""
                LEFT JOIN ""AspNetUsers"" u ON m.""CreatedById"" = u.""Id""
                WHERE to_tsvector('english', m.""Content"") @@ plainto_tsquery('english', {{0}})
                {(options.ForumId.HasValue ? "AND t.\"ForumId\" = {1}" : "")}
                {(!string.IsNullOrEmpty(options.UserId) ? $"AND m.\"CreatedById\" = {{{(options.ForumId.HasValue ? 2 : 1)}}}" : "")}
                {(options.StartDate.HasValue ? $"AND m.\"CreatedAtUtc\" >= {{{GetParamIndex(options, 0)}}}" : "")}
                {(options.EndDate.HasValue ? $"AND m.\"CreatedAtUtc\" <= {{{GetParamIndex(options, 1)}}}" : "")}
                ORDER BY score DESC
                LIMIT {{0}}";

            var parameters = new List<object> { searchQuery };
            var paramIndex = 1;

            if (options.ForumId.HasValue)
            {
                parameters.Add(options.ForumId.Value);
                paramIndex++;
            }

            if (!string.IsNullOrEmpty(options.UserId))
            {
                parameters.Add(options.UserId);
                paramIndex++;
            }

            if (options.StartDate.HasValue)
            {
                parameters.Add(options.StartDate.Value);
                paramIndex++;
            }

            if (options.EndDate.HasValue)
            {
                parameters.Add(options.EndDate.Value);
                paramIndex++;
            }

            parameters.Add(options.Limit);

            // Rebuild SQL with correct parameter indices
            sql = BuildFullTextSql(options, searchQuery);
            parameters = BuildFullTextParameters(options, searchQuery);

            var rows = await db.Set<FullTextSearchRow>()
                .FromSqlRaw(sql, parameters.ToArray())
                .ToListAsync(ct);

            return rows.Select(row => new SearchResult(
                MessageId: row.message_id,
                ThreadId: row.thread_id,
                ForumId: row.forum_id,
                ForumSlug: row.forum_slug,
                ThreadTitle: row.thread_title,
                MessageContent: row.message_content,
                AuthorName: row.author_name,
                CreatedAt: row.created_at,
                Score: row.score,
                SemanticScore: null,
                FullTextScore: row.score,
                Snippet: BuildSnippet(row.message_content, options.Query)
            )).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Full-text search failed for query: {Query}", options.Query);
            return new List<SearchResult>();
        }
    }

    private async Task<List<SearchResult>> SemanticSearchAsync(SearchOptions options, CancellationToken ct)
    {
        try
        {
            var matches = await embeddingService.SearchAsync(
                options.Query,
                options.ForumId,
                limit: options.Limit * 2, // Get more results for filtering
                ct
            );

            if (!matches.Any())
            {
                return new List<SearchResult>();
            }

            var messageIds = matches.Select(m => m.MessageId).ToList();

            var query = db.Messages.AsNoTracking()
                .Include(m => m.Thread)
                    .ThenInclude(t => t.Forum)
                .Include(m => m.CreatedBy)
                .Where(m => messageIds.Contains(m.Id));

            // Apply additional filters
            if (!string.IsNullOrEmpty(options.UserId))
            {
                query = query.Where(m => m.CreatedById == options.UserId);
            }

            if (options.StartDate.HasValue)
            {
                query = query.Where(m => m.CreatedAtUtc >= options.StartDate.Value);
            }

            if (options.EndDate.HasValue)
            {
                query = query.Where(m => m.CreatedAtUtc <= options.EndDate.Value);
            }

            var messages = await query.ToListAsync(ct);

            // Join with scores and create results
            var results = (from match in matches
                          join msg in messages on match.MessageId equals msg.Id
                          select new SearchResult(
                              MessageId: msg.Id,
                              ThreadId: msg.ForumThreadId,
                              ForumId: msg.Thread.ForumId,
                              ForumSlug: msg.Thread.Forum.Slug,
                              ThreadTitle: msg.Thread.Title,
                              MessageContent: msg.Content,
                              AuthorName: msg.CreatedBy?.UserName ?? "Anonymous",
                              CreatedAt: msg.CreatedAtUtc,
                              Score: match.Score,
                              SemanticScore: match.Score,
                              FullTextScore: null,
                              Snippet: BuildSnippet(msg.Content, options.Query)
                          )).ToList();

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Semantic search failed for query: {Query}", options.Query);
            return new List<SearchResult>();
        }
    }

    public async Task<List<(Guid ForumId, string ForumName, string ForumSlug)>> GetForumsAsync(CancellationToken ct = default)
    {
        return await db.Forums
            .AsNoTracking()
            .OrderBy(f => f.Name)
            .Select(f => new ValueTuple<Guid, string, string>(f.Id, f.Name, f.Slug))
            .ToListAsync(ct);
    }

    private static string BuildSnippet(string content, string? query)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;

        const int snippetLength = 240;
        const int contextLength = 60;

        if (string.IsNullOrEmpty(query) || content.Length <= snippetLength)
        {
            return content.Length > snippetLength
                ? content.Substring(0, snippetLength) + "…"
                : content;
        }

        // Try to find query terms in content
        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bestMatch = -1;

        foreach (var term in queryTerms)
        {
            var idx = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                bestMatch = idx;
                break;
            }
        }

        if (bestMatch < 0)
        {
            return content.Length > snippetLength
                ? content.Substring(0, snippetLength) + "…"
                : content;
        }

        var start = Math.Max(0, bestMatch - contextLength);
        var length = Math.Min(snippetLength, content.Length - start);
        var snippet = content.Substring(start, length);

        return (start > 0 ? "…" : "") + snippet + (start + length < content.Length ? "…" : "");
    }

    private static string BuildFullTextSql(SearchOptions options, string searchQuery)
    {
        var paramIndex = 0;
        var sql = $@"
            SELECT
                m.""Id"" as message_id,
                m.""ForumThreadId"" as thread_id,
                t.""ForumId"" as forum_id,
                f.""Slug"" as forum_slug,
                t.""Title"" as thread_title,
                m.""Content"" as message_content,
                COALESCE(u.""UserName"", 'Anonymous') as author_name,
                m.""CreatedAtUtc"" as created_at,
                ts_rank(to_tsvector('english', m.""Content""), plainto_tsquery('english', {{{paramIndex}}})) as score
            FROM ""Messages"" m
            INNER JOIN ""Threads"" t ON m.""ForumThreadId"" = t.""Id""
            INNER JOIN ""Forums"" f ON t.""ForumId"" = f.""Id""
            LEFT JOIN ""AspNetUsers"" u ON m.""CreatedById"" = u.""Id""
            WHERE to_tsvector('english', m.""Content"") @@ plainto_tsquery('english', {{{paramIndex}}})";

        paramIndex++;

        if (options.ForumId.HasValue)
        {
            sql += $" AND t.\"ForumId\" = {{{paramIndex}}}";
            paramIndex++;
        }

        if (!string.IsNullOrEmpty(options.UserId))
        {
            sql += $" AND m.\"CreatedById\" = {{{paramIndex}}}";
            paramIndex++;
        }

        if (options.StartDate.HasValue)
        {
            sql += $" AND m.\"CreatedAtUtc\" >= {{{paramIndex}}}";
            paramIndex++;
        }

        if (options.EndDate.HasValue)
        {
            sql += $" AND m.\"CreatedAtUtc\" <= {{{paramIndex}}}";
            paramIndex++;
        }

        sql += $" ORDER BY score DESC LIMIT {{{paramIndex}}}";

        return sql;
    }

    private static List<object> BuildFullTextParameters(SearchOptions options, string searchQuery)
    {
        var parameters = new List<object> { searchQuery };

        if (options.ForumId.HasValue)
            parameters.Add(options.ForumId.Value);

        if (!string.IsNullOrEmpty(options.UserId))
            parameters.Add(options.UserId);

        if (options.StartDate.HasValue)
            parameters.Add(options.StartDate.Value);

        if (options.EndDate.HasValue)
            parameters.Add(options.EndDate.Value);

        parameters.Add(options.Limit);

        return parameters;
    }

    private static int GetParamIndex(SearchOptions options, int dateParamOffset)
    {
        var index = 1; // Start after query param

        if (options.ForumId.HasValue) index++;
        if (!string.IsNullOrEmpty(options.UserId)) index++;

        return index + dateParamOffset;
    }

    // Helper class for raw SQL mapping
    private class FullTextSearchRow
    {
        public Guid message_id { get; set; }
        public Guid thread_id { get; set; }
        public Guid forum_id { get; set; }
        public string forum_slug { get; set; } = string.Empty;
        public string thread_title { get; set; } = string.Empty;
        public string message_content { get; set; } = string.Empty;
        public string author_name { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
        public double score { get; set; }
    }
}