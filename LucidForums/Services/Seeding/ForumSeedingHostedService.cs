using LucidForums.Hubs;
using LucidForums.Services.Ai;
using LucidForums.Services.Forum;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace LucidForums.Services.Seeding;

public class ForumSeedingHostedService(
    IForumSeedingQueue queue,
    IForumService forumService,
    IThreadService threadService,
    IMessageService messageService,
    ITextAiService ai,
    IHubContext<SeedingHub> hub
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await Broadcast(job.JobId, "start", $"Seeding forum '{job.ForumName}'...");

                // Create forum
                var forum = await forumService.CreateAsync(job.ForumName, job.ForumSlug, job.Description, "system", stoppingToken);
                await Broadcast(job.JobId, "forum", $"Created forum {forum.Name}", forum.Id.ToString());

                for (int t = 0; t < job.ThreadCount; t++)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    var titlePrompt = $"Generate a realistic, catchy forum thread title for a forum named '{job.ForumName}'. Return only the title.";
                    var title = await ai.GenerateAsync(new Models.Entities.Charter { Name = job.ForumName, Purpose = "Seed forum content" }, titlePrompt, ct: stoppingToken);

                    var contentPrompt = $"Write an engaging opening post for a thread titled '{title}'. 2-4 paragraphs, markdown allowed. Keep it under 250 words.";
                    var content = await ai.GenerateAsync(new Models.Entities.Charter { Name = job.ForumName, Purpose = "Seed forum content" }, contentPrompt, ct: stoppingToken);

                    var author = RandomAuthor();
                    var thread = await threadService.CreateAsync(forum.Id, TrimLine(title, 120), content, author, stoppingToken);
                    await Broadcast(job.JobId, "thread", $"Created thread '{thread.Title}' by {author}", thread.Id.ToString());

                    // Replies
                    for (int r = 0; r < job.RepliesPerThread; r++)
                    {
                        var replyPrompt = $"Write a realistic forum reply to the thread titled '{thread.Title}'. 1-2 short paragraphs. Keep it conversational and varying opinions.";
                        var reply = await ai.GenerateAsync(new Models.Entities.Charter { Name = job.ForumName, Purpose = "Seed forum content" }, replyPrompt, ct: stoppingToken);
                        var replyAuthor = RandomAuthor();
                        var msg = await messageService.ReplyAsync(thread.Id, null, reply, replyAuthor, stoppingToken);
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
        return hub.Clients.Group(SeedingHub.GroupName(jobId)).SendAsync("progress", new ForumSeedingProgress(jobId, stage, message, entityId));
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
