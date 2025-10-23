using LucidForums.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Forum;

public interface IThreadService
{
    Task<ForumThread?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ForumThread> CreateAsync(Guid forumId, string title, string content, string? authorId, string sourceLanguage = "en", CancellationToken ct = default);
    Task<List<ForumThread>> ListByForumAsync(Guid forumId, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<List<ForumThread>> ListLatestAsync(int skip = 0, int take = 10, CancellationToken ct = default);
}

public class ThreadService(LucidForums.Data.ApplicationDbContext db, LucidForums.Services.Analysis.ICharterScoringService charterScoring, LucidForums.Services.Search.IEmbeddingService embeddingService, LucidForums.Services.Analysis.ITagExtractionService tagExtractor) : IThreadService
{
    public Task<ForumThread?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return db.Threads
            .Include(t => t.Forum)
            .Include(t => t.RootMessage)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<ForumThread> CreateAsync(Guid forumId, string title, string content, string? authorId, string sourceLanguage = "en", CancellationToken ct = default)
    {
        // Validate author exists; otherwise use null to avoid FK violations
        string? validAuthorId = null;
        if (!string.IsNullOrWhiteSpace(authorId))
        {
            try
            {
                bool exists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == authorId, ct);
                if (exists) validAuthorId = authorId;
            }
            catch { }
        }

        var thread = new ForumThread
        {
            ForumId = forumId,
            Title = title,
            CreatedById = validAuthorId,
            SourceLanguage = sourceLanguage,
            CreatedAtUtc = DateTime.UtcNow
        };
        var root = new Message
        {
            Thread = thread,
            Content = content,
            CreatedById = validAuthorId,
            SourceLanguage = sourceLanguage,
            CreatedAtUtc = DateTime.UtcNow,
            Path = null,
        };
        db.Threads.Add(thread);
        db.Messages.Add(root);
        await db.SaveChangesAsync(ct);

        // Set root id after insert
        thread.RootMessageId = root.Id;
        db.Entry(thread).Property(x => x.RootMessageId).IsModified = true;
        await db.SaveChangesAsync(ct);

        // Centrally generate and store tags based on title + content (best-effort)
        try
        {
            var text = (title ?? string.Empty) + "\n\n" + (content ?? string.Empty);
            var tags = await tagExtractor.ExtractAsync(text, maxTags: 8, ct);
            if (tags is { Count: > 0 })
            {
                thread.Tags = tags.ToList();
                db.Entry(thread).Property(x => x.Tags).IsModified = true;
                await db.SaveChangesAsync(ct);
            }
        }
        catch { }

        // Compute charter scores (best-effort)
        try
        {
            var forum = await db.Forums.AsNoTracking().Include(f => f.Charter).FirstOrDefaultAsync(f => f.Id == forumId, ct);
            var charter = forum?.Charter;
            if (charter is not null)
            {
                var threadText = $"{title}\n\n{content}";
                var tScore = await charterScoring.ScoreAsync(charter, threadText, ct);
                var mScore = await charterScoring.ScoreAsync(charter, content, ct);
                if (tScore.HasValue)
                {
                    thread.CharterScore = tScore.Value;
                    db.Entry(thread).Property(x => x.CharterScore).IsModified = true;
                }
                if (mScore.HasValue)
                {
                    root.CharterScore = mScore.Value;
                    db.Entry(root).Property(x => x.CharterScore).IsModified = true;
                }
                await db.SaveChangesAsync(ct);
            }
        }
        catch { }

        // Fire-and-forget indexing of the root message embedding (important for seeded threads)
        _ = Task.Run(async () =>
        {
            try { await embeddingService.IndexMessageAsync(thread.RootMessageId!.Value, CancellationToken.None); }
            catch { /* swallow */ }
        });

        return thread;
    }

    public Task<List<ForumThread>> ListByForumAsync(Guid forumId, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        return db.Threads.Where(t => t.ForumId == forumId)
            .Include(t => t.Messages)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
    }

    public Task<List<ForumThread>> ListLatestAsync(int skip = 0, int take = 10, CancellationToken ct = default)
    {
        return db.Threads
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }
}
