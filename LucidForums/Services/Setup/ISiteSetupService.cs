namespace LucidForums.Services.Setup;

/// <summary>
/// Service for one-click site setup with sample data
/// </summary>
public interface ISiteSetupService
{
    /// <summary>
    /// Generates a complete site setup with forums, users, and content
    /// </summary>
    /// <param name="forumCount">Number of forums to create</param>
    /// <param name="usersPerForum">Number of users per forum</param>
    /// <param name="threadsPerForum">Number of threads per forum</param>
    /// <param name="repliesPerThread">Number of replies per thread</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Progress report</returns>
    Task<SiteSetupResult> GenerateSiteAsync(
        int forumCount = 12,
        int usersPerForum = 5,
        int threadsPerForum = 10,
        int repliesPerThread = 5,
        CancellationToken ct = default);
}

public class SiteSetupResult
{
    public bool Success { get; set; }
    public List<string> Messages { get; set; } = new();
    public int ForumsCreated { get; set; }
    public int UsersCreated { get; set; }
    public int ThreadsCreated { get; set; }
    public int MessagesCreated { get; set; }
    public List<string> Errors { get; set; } = new();
}
