using LucidForums.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Forum;

public interface IMessageService
{
    Task<Message?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Message> ReplyAsync(Guid threadId, Guid? parentMessageId, string content, string? authorId, CancellationToken ct = default);
    Task<List<Message>> ListByThreadAsync(Guid threadId, CancellationToken ct = default);
}

public class MessageService(LucidForums.Data.ApplicationDbContext db, LucidForums.Services.Search.IEmbeddingService embeddingService) : IMessageService
{
    public Task<Message?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return db.Messages.FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<Message> ReplyAsync(Guid threadId, Guid? parentMessageId, string content, string? authorId, CancellationToken ct = default)
    {
        var msg = new Message
        {
            ForumThreadId = threadId,
            ParentId = parentMessageId,
            Content = content,
            CreatedById = authorId,
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
