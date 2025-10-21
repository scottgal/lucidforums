using LucidForums.Hubs;
using LucidForums.Services.Ai;
using LucidForums.Services.Forum;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LucidForums.Services.Seeding;

public class ForumSeedingHostedService(
    IForumSeedingQueue queue,
    IServiceScopeFactory scopeFactory,
    ITextAiService ai,
    IHubContext<SeedingHub> hub,
    ISeedingProgressStore progressStore
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await Broadcast(job.JobId, "start", $"Seeding forum '{job.ForumName}'...");

                using var scope = scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                var forumService = sp.GetRequiredService<IForumService>();
                var threadService = sp.GetRequiredService<IThreadService>();
                var messageService = sp.GetRequiredService<IMessageService>();

                // Create forum
                var forum = await forumService.CreateAsync(job.ForumName, job.ForumSlug, job.Description, null, stoppingToken);
                await Broadcast(job.JobId, "forum", $"Created forum {forum.Name}", forum.Id.ToString());

                var seedCharter = new Models.Entities.Charter {
                    Name = string.IsNullOrWhiteSpace(job.ForumName) ? "Forum Seed" : job.ForumName,
                    Purpose = string.IsNullOrWhiteSpace(job.CharterDescription)
                        ? ($"Generate realistic forum content. Site purpose: {job.SitePurpose ?? job.Description ?? job.ForumName}. Keep tone friendly, inclusive, and on-topic.")
                        : job.CharterDescription
                };

                for (int t = 0; t < job.ThreadCount; t++)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    var titlePrompt = $"Generate a realistic, catchy forum thread title for a forum named '{job.ForumName}'. Consider site purpose: '{job.SitePurpose ?? job.Description}'. Return only the title.";
                    var title = await ai.GenerateAsync(seedCharter, titlePrompt, ct: stoppingToken);

                    var contentPrompt = $"Write an engaging opening post for a thread titled '{title}'. It should fit the forum purpose: '{job.SitePurpose ?? job.Description}'. 2-4 paragraphs, markdown allowed. Keep it under 250 words.";
                    var content = await ai.GenerateAsync(seedCharter, contentPrompt, ct: stoppingToken);

                    var author = RandomAuthor();
                    var thread = await threadService.CreateAsync(forum.Id, TrimLine(title, 120), content, null, stoppingToken);
                    await Broadcast(job.JobId, "thread", $"Created thread '{thread.Title}' by {author}", thread.Id.ToString());

                    // Replies
                    for (int r = 0; r < job.RepliesPerThread; r++)
                    {
                        var replyPrompt = $"Write a realistic forum reply to the thread titled '{thread.Title}'. 1-2 short paragraphs. Keep it conversational and varying opinions.";
                        var reply = await ai.GenerateAsync(new Models.Entities.Charter { Name = job.ForumName, Purpose = "Seed forum content" }, replyPrompt, ct: stoppingToken);
                        var replyAuthor = RandomAuthor();
                        var msg = await messageService.ReplyAsync(thread.Id, null, reply, null, stoppingToken);
                        await Broadcast(job.JobId, "reply", $"Reply by {replyAuthor}", msg.Id.ToString());
                    }
                }

                await Broadcast(job.JobId, "done", "Seeding complete.");
            }
            catch (Exception ex)
            {
                await Broadcast(job.JobId, "error", ex.Message);
            }
        }
    }

    private Task Broadcast(Guid jobId, string stage, string message, string? entityId = null)
    {
        var evt = new ForumSeedingProgress(jobId, stage, message, entityId);
        progressStore.Append(evt);
        return hub.Clients.Group(SeedingHub.GroupName(jobId)).SendAsync("progress", evt);
    }

    private static string TrimLine(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var s = text.Trim().Replace("\r", " ").Replace("\n", " ");
        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }

    private static readonly string[] SampleFirstNames = [
        "Alex","Sam","Jamie","Taylor","Jordan","Casey","Riley","Morgan","Cameron","Avery",
        "Quinn","Reese","Drew","Hayden","Rowan","Parker","Emerson","Finley","Sage","Skyler"
    ];
    private static readonly string[] SampleLastNames = [
        "Lee","Patel","Garcia","Smith","Nguyen","Brown","Kim","Lopez","Martin","Davis",
        "Rodriguez","Wilson","Anderson","Thomas","Hernandez","Moore","Jackson","Thompson","White","Clark"
    ];

    private static string RandomAuthor()
    {
        var rng = Random.Shared;
        return $"{SampleFirstNames[rng.Next(SampleFirstNames.Length)]} {SampleLastNames[rng.Next(SampleLastNames.Length)]}";
    }
}
