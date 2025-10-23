using LucidForums.Hubs;
using LucidForums.Services.Ai;
using LucidForums.Services.Forum;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using LucidForums.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Seeding;

public class ForumSeedingHostedService(
    IForumSeedingQueue queue,
    IServiceScopeFactory scopeFactory,
    ITextAiService ai,
    LucidForums.Services.Search.IEmbeddingService embeddings,
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

                // Create forum
                var forum = await forumService.CreateAsync(job.ForumName, job.ForumSlug, job.Description, null, "en", null, stoppingToken);
                await Broadcast(job.JobId, "forum", $"Created forum {forum.Name}", forum.Id.ToString());

                // Choose a charter to guide content tone
                var db = sp.GetRequiredService<ApplicationDbContext>();
                Models.Entities.Charter? selectedCharter = null;
                try
                {
                    var charters = await db.Charters.AsNoTracking().OrderBy(c => c.Name).ToListAsync(stoppingToken);
                    if (charters.Count > 0)
                    {
                        // Simple selection: pick one based on hash of forum Id for determinism across runs
                        var index = Math.Abs(forum.Id.GetHashCode()) % charters.Count;
                        selectedCharter = charters[index];
                        await Broadcast(job.JobId, "charter", $"Selected charter: '{selectedCharter.Name}'", selectedCharter.Id.ToString());

                        // Attach charter to forum
                        var tracked = await db.Forums.FirstOrDefaultAsync(f => f.Id == forum.Id, stoppingToken);
                        if (tracked is not null)
                        {
                            tracked.CharterId = selectedCharter.Id;
                            await db.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
                catch
                {
                    // ignore charter selection errors; will fallback to ad-hoc charter
                }

                var seedCharter = selectedCharter ?? new Models.Entities.Charter {
                    Name = string.IsNullOrWhiteSpace(job.ForumName) ? "Forum Seed" : job.ForumName,
                    Purpose = string.IsNullOrWhiteSpace(job.CharterDescription)
                        ? ($"Generate realistic forum content. Site purpose: {job.SitePurpose ?? job.Description ?? job.ForumName}. Keep tone friendly, inclusive, and on-topic.")
                        : job.CharterDescription
                };

                // Generate threads in parallel with controlled concurrency
                int maxConcurrency = GetMaxConcurrency();
                var throttler = new SemaphoreSlim(maxConcurrency);
                var tasks = new List<Task>();

                // Track titles we have already used during this seeding job to avoid duplicates
                var usedTitles = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                // Maintain embeddings of accepted titles to enforce semantic diversity
                var titleEmbeddings = new System.Collections.Concurrent.ConcurrentBag<float[]>();
                const double titleSimThreshold = 0.90; // higher = allow closer; 0.90 is a good balance
                int uniqueSuffixSeq = 0;

                static double CosineSim(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
                {
                    double dot = 0, na = 0, nb = 0;
                    int len = Math.Min(a.Length, b.Length);
                    for (int i = 0; i < len; i++)
                    {
                        double x = a[i];
                        double y = b[i];
                        dot += x * y;
                        na += x * x;
                        nb += y * y;
                    }
                    if (na == 0 || nb == 0) return 0;
                    return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
                }

                for (int t = 0; t < job.ThreadCount; t++)
                {
                    await throttler.WaitAsync(stoppingToken);
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            stoppingToken.ThrowIfCancellationRequested();

                            // Generate a unique title with a few retries to avoid duplicates across concurrent tasks
                            string title = string.Empty;
                            string rawTitle = string.Empty;
                            const int maxTitleAttempts = 6;
                            float[]? acceptedEmb = null;
                            for (int attempt = 0; attempt < maxTitleAttempts; attempt++)
                            {
                                var varNote = attempt == 0 ? string.Empty : $" Please provide a distinctly different angle. Variation seed: {Guid.NewGuid():N}";
                                var titlePrompt = $"Generate a realistic, catchy forum thread title for a forum named '{job.ForumName}'. Consider site purpose: '{job.SitePurpose ?? job.Description}'. Return only the title.{varNote}";
                                rawTitle = await ai.GenerateAsync(seedCharter, titlePrompt, ct: stoppingToken);
                                title = TrimLine(rawTitle, 120);

                                // Compute embedding and compare with previous titles to ensure semantic diversity
                                float[]? emb = null;
                                try { emb = await embeddings.EmbedAsync(title, stoppingToken); } catch { /* fall through if embedding not available */ }

                                bool diverseEnough = true;
                                if (emb is not null)
                                {
                                    var snapshot = titleEmbeddings.ToArray();
                                    foreach (var prev in snapshot)
                                    {
                                        var sim = CosineSim(emb, prev);
                                        if (sim >= titleSimThreshold)
                                        {
                                            diverseEnough = false;
                                            break;
                                        }
                                    }
                                }

                                if (diverseEnough && usedTitles.TryAdd(title, 0))
                                {
                                    if (emb is not null) { titleEmbeddings.Add(emb); acceptedEmb = emb; }
                                    break;
                                }
                            }
                            if (!usedTitles.ContainsKey(title))
                            {
                                // all attempts collided; add a unique suffix to ensure distinct title
                                var suffix = System.Threading.Interlocked.Increment(ref uniqueSuffixSeq);
                                title = TrimLine($"{(string.IsNullOrWhiteSpace(title) ? rawTitle : title)} (Discussion {suffix})", 120);
                                usedTitles.TryAdd(title, 0);
                            }

                            var contentPrompt = $"Write an engaging opening post for a thread titled '{title}'. It should fit the forum purpose: '{job.SitePurpose ?? job.Description}'. Start with a clear, natural-sounding question in the first sentence that invites answers. 2-4 short paragraphs, markdown allowed. Keep it under 250 words. Avoid sounding like marketing copy.";
                            var content = await ai.GenerateAsync(seedCharter, contentPrompt, ct: stoppingToken);

                            if (job.IncludeEmoticons)
                            {
                                title = MaybeAddEmoticonToTitle(title);
                                content = AddEmoticonsToText(content);
                            }

                            var author = RandomAuthor();
                            using var tScope = scopeFactory.CreateScope();
                            var tsp = tScope.ServiceProvider;
                            var threadService2 = tsp.GetRequiredService<IThreadService>();
                            var messageService2 = tsp.GetRequiredService<IMessageService>();

                            var thread = await threadService2.CreateAsync(forum.Id, TrimLine(title, 120), content, null, "en", stoppingToken);
                            await Broadcast(job.JobId, "thread", $"Created thread '{thread.Title}' by {author}", thread.Id.ToString());

                            // Generate discovery-friendly tags using AI (overwrite heuristic tags if successful)
                            try
                            {
                                var tagsPrompt = $"Based on the thread title and opening post below, generate 3-7 concise tags that help users find this discussion. Use useful categories like topics, problem/intent, audience, and domain-specific keywords. Output ONLY a JSON array of lowercase strings, no explanations.\\n\\nTitle: '{thread.Title}'\\n\\nOpening post:\\n{TrimLine(content, 1000)}";
                                var tagsJson = await ai.GenerateAsync(seedCharter, tagsPrompt, ct: stoppingToken);
                                // Try parse JSON array of strings
                                var tags = new List<string>();
                                try
                                {
                                    using var doc = System.Text.Json.JsonDocument.Parse(tagsJson);
                                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                                    {
                                        foreach (var el in doc.RootElement.EnumerateArray())
                                        {
                                            if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                                            {
                                                var tag = el.GetString();
                                                if (!string.IsNullOrWhiteSpace(tag)) tags.Add(tag!.Trim());
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                    // Fallback: split by commas/newlines
                                    foreach (var part in tagsJson.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
                                    {
                                        var tag = part.Trim().Trim('#').Trim().ToLowerInvariant();
                                        if (tag.Length > 0 && tag.Length <= 40) tags.Add(tag);
                                    }
                                }
                                // Deduplicate and cap
                                var finalTags = tags.Select(t => t.Trim().Trim('#').ToLowerInvariant())
                                                    .Where(t => t.Length > 1)
                                                    .Distinct()
                                                    .Take(8)
                                                    .ToList();
                                if (finalTags.Count > 0)
                                {
                                    var db2 = tsp.GetRequiredService<LucidForums.Data.ApplicationDbContext>();
                                    var trackedThread = await db2.Threads.FirstOrDefaultAsync(x => x.Id == thread.Id, stoppingToken);
                                    if (trackedThread is not null)
                                    {
                                        trackedThread.Tags = finalTags;
                                        await db2.SaveChangesAsync(stoppingToken);
                                    }
                                }
                            }
                            catch { }

                            // Prepare list of prior messages to enable threaded, responsive replies
                            var priorMessages = new List<(Guid Id, string Content)> { (thread.RootMessage.Id, thread.RootMessage.Content) };

                            // Replies (sequential within a thread to avoid DbContext contention)
                            for (int r = 0; r < job.RepliesPerThread; r++)
                            {
                                var rng = Random.Shared;
                                // Choose a parent: usually the root, sometimes a prior reply
                                (Guid Id, string Content) parent = priorMessages[0];
                                if (priorMessages.Count > 1 && rng.NextDouble() < 0.6)
                                {
                                    // 60% chance to reply to a non-root prior message
                                    var idx = rng.Next(1, priorMessages.Count);
                                    parent = priorMessages[idx];
                                }

                                // Build a context-aware prompt that responds directly to the parent message
                                var parentSnippet = TrimLine(parent.Content, 220);
                                var rootSnippet = TrimLine(thread.RootMessage.Content, 220);
                                string replyPrompt;
                                if (parent.Id == thread.RootMessage.Id)
                                {
                                    replyPrompt = $"Write a realistic forum reply to the opening post in the thread '{thread.Title}'. Opening post: '{rootSnippet}'. Answer the question directly, add a new angle or example, and keep it conversational. 1-2 short paragraphs, under 120 words. Avoid repeating the question verbatim.";
                                }
                                else
                                {
                                    replyPrompt = $"Write a realistic forum reply that responds directly to this comment in the thread '{thread.Title}': '{parentSnippet}'. You may reference the opening post for context: '{rootSnippet}'. Acknowledge a specific point from the comment (you can quote a short phrase using >), then add value or a counterpoint. Keep it friendly and on-topic. 1-2 short paragraphs, under 120 words.";
                                }

                                var reply = await ai.GenerateAsync(seedCharter, replyPrompt, ct: stoppingToken);
                                if (job.IncludeEmoticons)
                                {
                                    reply = AddEmoticonsToText(reply);
                                }
                                var replyAuthor = RandomAuthor();
                                var msg = await messageService2.ReplyAsync(thread.Id, parent.Id, reply, null, "en", stoppingToken);
                                priorMessages.Add((msg.Id, reply));
                                await Broadcast(job.JobId, "reply", $"Reply by {replyAuthor}", msg.Id.ToString());
                            }
                        }
                        catch (Exception ex)
                        {
                            await Broadcast(job.JobId, "error", ex.Message);
                        }
                        finally
                        {
                            throttler.Release();
                        }
                    }, stoppingToken);
                    tasks.Add(task);
                }

                await Task.WhenAll(tasks);

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

    private static readonly string[] Emoticons = new[] { "🙂", "😄", "😅", "🤔", "🙌", "👍", "🌟", "✨", "😴", "😎", "😉", "😂", "🤝", "🎯" };

    private static string MaybeAddEmoticonToTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title ?? string.Empty;
        var rng = Random.Shared;
        // 40% chance to append one emoticon if short enough
        if (rng.NextDouble() < 0.4 && title.Length < 110)
        {
            var emo = Emoticons[rng.Next(Emoticons.Length)];
            // Avoid duplicate emoticon if already ends with one
            if (!char.IsSurrogate(title[^1]))
            {
                return title.TrimEnd() + " " + emo;
            }
        }
        return title;
    }

    private static string AddEmoticonsToText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
        var rng = Random.Shared;
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        int added = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            // 50% chance per non-empty line, cap total to 3 additions
            if (rng.NextDouble() < 0.5 && added < 3)
            {
                var emo = Emoticons[rng.Next(Emoticons.Length)];
                lines[i] = line.TrimEnd() + " " + emo;
                added++;
            }
        }
        return string.Join('\n', lines);
    }

    private static int GetMaxConcurrency()
    {
        var env = Environment.GetEnvironmentVariable("LUCIDFORUMS_SEEDING_MAX_CONCURRENCY");
        if (int.TryParse(env, out var v) && v > 0)
        {
            return Math.Clamp(v, 1, 64);
        }
        // Reasonable default: up to CPU count, but capped to avoid overwhelming DB
        var def = Math.Clamp(Environment.ProcessorCount, 2, 12);
        return def;
    }
}
