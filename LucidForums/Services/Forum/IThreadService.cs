using LucidForums.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Forum;

public interface IThreadService
{
    Task<ForumThread?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ForumThread> CreateAsync(Guid forumId, string title, string content, string? authorId, CancellationToken ct = default);
    Task<List<ForumThread>> ListByForumAsync(Guid forumId, int skip = 0, int take = 50, CancellationToken ct = default);
}

public class ThreadService(LucidForums.Data.ApplicationDbContext db) : IThreadService
{
    public Task<ForumThread?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return db.Threads
            .Include(t => t.Forum)
            .Include(t => t.RootMessage)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<ForumThread> CreateAsync(Guid forumId, string title, string content, string? authorId, CancellationToken ct = default)
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
            CreatedAtUtc = DateTime.UtcNow
        };
        var root = new Message
        {
            Thread = thread,
            Content = content,
            CreatedById = validAuthorId,
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
        return thread;
    }

    public Task<List<ForumThread>> ListByForumAsync(Guid forumId, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        return db.Threads.Where(t => t.ForumId == forumId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
    }
}
