using LucidForums.Data;
using LucidForums.Hubs;
using LucidForums.Models.Entities;
using LucidForums.Services.Ai;
using LucidForums.Services.Forum;
using LucidForums.Services.Search;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Setup;

public class SiteSetupService : ISiteSetupService
{
    private readonly UserManager<User> _userManager;
    private readonly IForumService _forumService;
    private readonly IThreadService _threadService;
    private readonly IMessageService _messageService;
    private readonly ITextAiService _ai;
    private readonly IEmbeddingService _embedding;
    private readonly IHubContext<SetupHub> _setupHub;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SiteSetupService> _logger;

    // Sample user data
    private static readonly string[] FirstNames = {
        "Alice", "Bob", "Charlie", "Diana", "Eve", "Frank", "Grace", "Henry",
        "Iris", "Jack", "Kate", "Leo", "Maya", "Noah", "Olivia", "Peter",
        "Quinn", "Rachel", "Sam", "Tara", "Uma", "Victor", "Wendy", "Xavier",
        "Yara", "Zoe", "Alex", "Blake", "Cameron", "Drew"
    };

    private static readonly string[] LastNames = {
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis",
        "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson", "Thomas",
        "Taylor", "Moore", "Jackson", "Martin", "Lee", "Perez", "Thompson", "White",
        "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson"
    };

    private static readonly string[] ForumThemes = {
        "Gaming", "Technology", "Science", "Arts", "Music", "Movies", "Books",
        "Cooking", "Travel", "Fitness", "Photography", "DIY", "Pets", "Gardening",
        "Sports", "Fashion", "History", "Philosophy", "Politics", "Environment"
    };

    // Language codes and their display names
    private static readonly Dictionary<string, string> SupportedLanguages = new()
    {
        { "en", "English" },
        { "es", "Spanish" },
        { "fr", "French" },
        { "de", "German" },
        { "ja", "Japanese" },
        { "zh", "Chinese" },
        { "pt", "Portuguese" },
        { "it", "Italian" },
        { "ru", "Russian" },
        { "ar", "Arabic" }
    };

    public SiteSetupService(
        UserManager<User> userManager,
        IForumService forumService,
        IThreadService threadService,
        IMessageService messageService,
        ITextAiService ai,
        IEmbeddingService embedding,
        IHubContext<SetupHub> setupHub,
        ApplicationDbContext db,
        ILogger<SiteSetupService> logger)
    {
        _userManager = userManager;
        _forumService = forumService;
        _threadService = threadService;
        _messageService = messageService;
        _ai = ai;
        _embedding = embedding;
        _setupHub = setupHub;
        _db = db;
        _logger = logger;
    }

    public async Task<SiteSetupResult> GenerateSiteAsync(
        int forumCount = 12,
        int usersPerForum = 5,
        int threadsPerForum = 10,
        int repliesPerThread = 5,
        CancellationToken ct = default)
    {
        var result = new SiteSetupResult { Success = true };

        try
        {
            await SendProgress("Starting site setup...", 0);

            // Calculate total steps for progress tracking
            int totalSteps = forumCount + (usersPerForum * forumCount) + (forumCount * threadsPerForum) + (forumCount * threadsPerForum * repliesPerThread);
            int completedSteps = 0;

            // Get available charters
            var charters = await _db.Charters.AsNoTracking().ToListAsync(ct);
            if (charters.Count == 0)
            {
                await SendProgress("Creating default charter...", 0);
                var defaultCharter = new Charter
                {
                    Name = "General Discussion",
                    Purpose = "Open and respectful discussion on all topics",
                    Rules = new List<string> { "Be respectful", "Stay on topic", "No spam" },
                    Behaviors = new List<string> { "Be helpful", "Share knowledge", "Welcome newcomers" }
                };
                _db.Charters.Add(defaultCharter);
                await _db.SaveChangesAsync(ct);
                charters.Add(defaultCharter);
            }

            // Create forums in multiple languages
            await SendProgress($"Creating {forumCount} forums in various languages...", CalculateProgress(completedSteps, totalSteps));
            var createdForums = new List<Models.Entities.Forum>();
            var usedThemes = new HashSet<string>();
            var languages = SupportedLanguages.Keys.ToArray();

            for (int i = 0; i < forumCount && i < ForumThemes.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                var theme = ForumThemes[i];
                usedThemes.Add(theme);

                // Cycle through languages to create diverse forums
                var languageCode = languages[i % languages.Length];
                var languageName = SupportedLanguages[languageCode];

                var charter = charters[Random.Shared.Next(charters.Count)];
                var forumName = await GenerateForumNameAsync(theme, languageCode, languageName, usedThemes, ct);
                var description = await GenerateForumDescriptionAsync(forumName, theme, languageCode, languageName, ct);
                var slug = Slugify(forumName);

                // Ensure unique slug
                int suffix = 1;
                var originalSlug = slug;
                while (await _db.Forums.AnyAsync(f => f.Slug == slug, ct))
                {
                    slug = $"{originalSlug}-{suffix++}";
                }

                var forum = await _forumService.CreateAsync(forumName, slug, description, null, sourceLanguage: languageCode, charterId: charter.Id, ct: ct);
                createdForums.Add(forum);
                result.ForumsCreated++;
                completedSteps++;
                await SendProgress($"Created forum: {forumName} ({languageName})", CalculateProgress(completedSteps, totalSteps));
            }

            // Create users
            await SendProgress($"Creating {usersPerForum * forumCount} users...", CalculateProgress(completedSteps, totalSteps));
            var createdUsers = new List<User>();
            var usedUsernames = new HashSet<string>();

            for (int i = 0; i < usersPerForum * forumCount; i++)
            {
                ct.ThrowIfCancellationRequested();

                var firstName = FirstNames[Random.Shared.Next(FirstNames.Length)];
                var lastName = LastNames[Random.Shared.Next(LastNames.Length)];
                var username = $"{firstName.ToLower()}.{lastName.ToLower()}";

                // Ensure unique username
                int suffix = 1;
                var originalUsername = username;
                while (usedUsernames.Contains(username) || await _userManager.FindByNameAsync(username) != null)
                {
                    username = $"{originalUsername}{suffix++}";
                }
                usedUsernames.Add(username);

                var user = new User
                {
                    UserName = username,
                    Email = $"{username}@example.com",
                    EmailConfirmed = true
                };

                var createResult = await _userManager.CreateAsync(user, "Password123!");
                if (createResult.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, "User");
                    createdUsers.Add(user);
                    result.UsersCreated++;
                    completedSteps++;

                    if (i % 5 == 0) // Send progress every 5 users
                    {
                        await SendProgress($"Created {result.UsersCreated} users...", CalculateProgress(completedSteps, totalSteps));
                    }
                }
                else
                {
                    result.Errors.Add($"Failed to create user {username}: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
                }
            }

            await SendProgress($"Created {createdUsers.Count} users", CalculateProgress(completedSteps, totalSteps));

            // Create threads and messages for each forum (in the forum's language)
            foreach (var forum in createdForums)
            {
                ct.ThrowIfCancellationRequested();

                var forumLanguage = forum.SourceLanguage ?? "en";
                var languageName = SupportedLanguages.GetValueOrDefault(forumLanguage, "English");
                await SendProgress($"Populating forum: {forum.Name} ({languageName})...", CalculateProgress(completedSteps, totalSteps));

                // Assign some users to this forum
                var forumUsers = createdUsers
                    .OrderBy(_ => Random.Shared.Next())
                    .Take(usersPerForum)
                    .ToList();

                for (int t = 0; t < threadsPerForum; t++)
                {
                    ct.ThrowIfCancellationRequested();

                    var author = forumUsers[Random.Shared.Next(forumUsers.Count)];
                    var title = await GenerateThreadTitleAsync(forum.Name, forumLanguage, languageName, ct);
                    var content = await GenerateThreadContentAsync(title, forum.Name, forumLanguage, languageName, ct);

                    var thread = await _threadService.CreateAsync(forum.Id, title, content, author.Id, forumLanguage, ct);
                    result.ThreadsCreated++;
                    completedSteps++;

                    // Generate embedding for the thread's root message
                    var rootMessage = await _db.Messages.FirstOrDefaultAsync(m => m.Id == thread.RootMessageId, ct);
                    if (rootMessage != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _embedding.IndexMessageAsync(rootMessage.Id);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to generate embedding for message {MessageId}", rootMessage.Id);
                            }
                        }, CancellationToken.None);
                    }

                    // Create replies in the same language
                    for (int r = 0; r < repliesPerThread; r++)
                    {
                        ct.ThrowIfCancellationRequested();

                        var replyAuthor = forumUsers[Random.Shared.Next(forumUsers.Count)];
                        var replyContent = await GenerateReplyContentAsync(title, content, forumLanguage, languageName, ct);

                        if (rootMessage != null)
                        {
                            var reply = await _messageService.ReplyAsync(thread.Id, rootMessage.Id, replyContent, replyAuthor.Id, forumLanguage, ct);
                            result.MessagesCreated++;
                            completedSteps++;

                            // Generate embedding for the reply
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await _embedding.IndexMessageAsync(reply.Id);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to generate embedding for message {MessageId}", reply.Id);
                                }
                            }, CancellationToken.None);
                        }
                    }

                    if (t % 3 == 0) // Send progress every 3 threads
                    {
                        await SendProgress($"Created {result.ThreadsCreated} threads in {forum.Name}...", CalculateProgress(completedSteps, totalSteps));
                    }
                }
            }

            await SendProgress("Site setup completed successfully!", 100);
            result.Messages.Add($"Summary: {result.ForumsCreated} forums, {result.UsersCreated} users, {result.ThreadsCreated} threads, {result.MessagesCreated} messages");
            _logger.LogInformation("Site setup completed: {Forums} forums, {Users} users, {Threads} threads, {Messages} messages",
                result.ForumsCreated, result.UsersCreated, result.ThreadsCreated, result.MessagesCreated);

            // Send completion message via SignalR
            await SendComplete(
                $"Successfully created {result.ForumsCreated} forums, {result.UsersCreated} users, {result.ThreadsCreated} threads, and {result.MessagesCreated} messages!",
                result);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Setup failed: {ex.Message}");
            _logger.LogError(ex, "Site setup failed");
            await SendError($"Setup failed: {ex.Message}");
        }

        return result;
    }

    private async Task SendProgress(string message, int percentComplete)
    {
        try
        {
            await _setupHub.Clients.All.SendAsync("ProgressUpdate", message, percentComplete);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send progress update via SignalR");
        }
    }

    private async Task SendComplete(string message, object result)
    {
        try
        {
            await _setupHub.Clients.All.SendAsync("SetupComplete", message, result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send completion message via SignalR");
        }
    }

    private async Task SendError(string message)
    {
        try
        {
            await _setupHub.Clients.All.SendAsync("SetupError", message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send error via SignalR");
        }
    }

    private static int CalculateProgress(int completed, int total)
    {
        if (total == 0) return 0;
        return Math.Min(100, (int)((completed / (double)total) * 100));
    }

    private async Task<string> GenerateForumNameAsync(string theme, string languageCode, string languageName, HashSet<string> usedThemes, CancellationToken ct)
    {
        try
        {
            var charter = new Charter { Name = "ForumGenerator", Purpose = "Generate forum names" };
            var usedList = string.Join(", ", usedThemes);
            var prompt = $"Generate a creative, concise forum name for a {theme} community IN {languageName}. Avoid these used themes: {usedList}. Return only the forum name in {languageName}, no quotes or extra text.";
            var name = await _ai.GenerateAsync(charter, prompt, ct: ct);
            return string.IsNullOrWhiteSpace(name) ? $"{theme} Community" : name.Trim();
        }
        catch
        {
            return $"{theme} Community";
        }
    }

    private async Task<string> GenerateForumDescriptionAsync(string forumName, string theme, string languageCode, string languageName, CancellationToken ct)
    {
        try
        {
            var charter = new Charter { Name = "ForumGenerator", Purpose = "Generate forum descriptions" };
            var prompt = $"Write a one-sentence description IN {languageName} for a forum called '{forumName}' about {theme}. Keep it under 150 characters. Return only the description in {languageName}.";
            var desc = await _ai.GenerateAsync(charter, prompt, ct: ct);
            return string.IsNullOrWhiteSpace(desc) ? $"A community for discussing {theme.ToLower()}" : desc.Trim();
        }
        catch
        {
            return $"A community for discussing {theme.ToLower()}";
        }
    }

    private async Task<string> GenerateThreadTitleAsync(string forumName, string languageCode, string languageName, CancellationToken ct)
    {
        try
        {
            var charter = new Charter { Name = "ContentGenerator", Purpose = "Generate thread titles" };
            var prompt = $"Generate a discussion thread title IN {languageName} for the '{forumName}' forum. Make it interesting and relevant. Return only the title in {languageName}, max 100 characters.";
            var title = await _ai.GenerateAsync(charter, prompt, ct: ct);
            return string.IsNullOrWhiteSpace(title) ? "General Discussion" : title.Trim().TrimEnd('.', '!', '?');
        }
        catch
        {
            return "General Discussion";
        }
    }

    private async Task<string> GenerateThreadContentAsync(string title, string forumName, string languageCode, string languageName, CancellationToken ct)
    {
        try
        {
            var charter = new Charter { Name = "ContentGenerator", Purpose = "Generate thread content" };
            var prompt = $"Write an opening post IN {languageName} for a forum thread titled '{title}' in the '{forumName}' forum. Be conversational and engaging. 2-3 sentences max. Write entirely in {languageName}.";
            var content = await _ai.GenerateAsync(charter, prompt, ct: ct);
            return string.IsNullOrWhiteSpace(content) ? "What are your thoughts on this topic?" : content.Trim();
        }
        catch
        {
            return "What are your thoughts on this topic?";
        }
    }

    private async Task<string> GenerateReplyContentAsync(string threadTitle, string originalContent, string languageCode, string languageName, CancellationToken ct)
    {
        try
        {
            var charter = new Charter { Name = "ContentGenerator", Purpose = "Generate replies" };
            var prompt = $"Write a forum reply IN {languageName} to a thread titled '{threadTitle}'. Be helpful and conversational. 1-2 sentences. Write entirely in {languageName}.";
            var reply = await _ai.GenerateAsync(charter, prompt, ct: ct);
            return string.IsNullOrWhiteSpace(reply) ? "Interesting point! I'd like to hear more perspectives on this." : reply.Trim();
        }
        catch
        {
            return "Interesting point! I'd like to hear more perspectives on this.";
        }
    }

    private static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "forum";

        var slug = text.ToLowerInvariant().Trim();
        slug = new string(slug.Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' ? c : '-').ToArray());
        slug = slug.Replace(' ', '-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
