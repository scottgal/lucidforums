using System.Text;
using System.Linq;
using LucidForums.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Forum;

public interface IMessageService
{
    Task<Message?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Message> ReplyAsync(Guid threadId, Guid? parentMessageId, string content, string? authorId, string sourceLanguage = "en", CancellationToken ct = default);
    Task<List<Message>> ListByThreadAsync(Guid threadId, CancellationToken ct = default);
}

public class MessageService(LucidForums.Data.ApplicationDbContext db, LucidForums.Services.Search.IEmbeddingService embeddingService, LucidForums.Services.Analysis.ITagExtractionService tagExtractor, LucidForums.Services.Analysis.IToneAdvisor toneAdvisor, LucidForums.Services.Analysis.ICharterScoringService charterScoring, LucidForums.Services.Translation.IContentTranslationQueue translationQueue) : IMessageService
{
    public Task<Message?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return db.Messages.FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<Message> ReplyAsync(Guid threadId, Guid? parentMessageId, string content, string? authorId, string sourceLanguage = "en", CancellationToken ct = default)
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

        var msg = new Message
        {
            ForumThreadId = threadId,
            ParentId = parentMessageId,
            Content = content,
            CreatedById = validAuthorId,
            SourceLanguage = sourceLanguage,
            CreatedAtUtc = DateTime.UtcNow
        };

        // Basic path building: append child id to parent path (not perfect without transaction, but OK for MVP)
        var parentPath = parentMessageId.HasValue
            ? (await db.Messages.AsNoTracking().Where(x => x.Id == parentMessageId.Value).Select(x => x.Path).FirstOrDefaultAsync(ct))
            : null;

        db.Messages.Add(msg);
        await db.SaveChangesAsync(ct);

        msg.Path = string.IsNullOrEmpty(parentPath) ? msg.Id.ToString("N") : parentPath + "." + msg.Id.ToString("N");
        db.Entry(msg).Property(x => x.Path).IsModified = true;
        await db.SaveChangesAsync(ct);

        // Queue message for translation into all available languages
        translationQueue.QueueMessageTranslation(msg.Id, msg.Content);

        // Extract tags and tone/charter advice, but do NOT modify message content
        try
        {
            var tags = await tagExtractor.ExtractAsync(msg.Content, 5, ct);
            var advice = await toneAdvisor.CreateAdviceAsync(msg.Content, ct);
            // Intentionally ignored for now: we may store/display elsewhere in the future
        }
        catch { /* best-effort only */ }

        // Compute and store charter score (best-effort)
        try
        {
            var threadWithForum = await db.Threads.AsNoTracking()
                .Where(t => t.Id == threadId)
                .Select(t => new { t.Id, t.ForumId })
                .FirstOrDefaultAsync(ct);
            if (threadWithForum is not null)
            {
                var forum = await db.Forums.AsNoTracking().Include(f => f.Charter)
                    .FirstOrDefaultAsync(f => f.Id == threadWithForum.ForumId, ct);
                var charter = forum?.Charter;
                if (charter is not null)
                {
                    var score = await charterScoring.ScoreAsync(charter, msg.Content, ct);
                    if (score.HasValue)
                    {
                        msg.CharterScore = score.Value;
                        db.Entry(msg).Property(x => x.CharterScore).IsModified = true;
                        await db.SaveChangesAsync(ct);
                    }
                }
            }
        }
        catch { }

        // Fire-and-forget indexing (do not block reply creation)
        _ = Task.Run(async () =>
        {
            try { await embeddingService.IndexMessageAsync(msg.Id, CancellationToken.None); }
            catch { /* swallow */ }
        });

        return msg;
    }

    public Task<List<Message>> ListByThreadAsync(Guid threadId, CancellationToken ct = default)
    {
        return db.Messages.Where(m => m.ForumThreadId == threadId)
            .OrderBy(m => m.Path)
            .ToListAsync(ct);
    }
}
