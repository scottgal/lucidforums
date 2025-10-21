using LucidForums.Models.Dtos;
using LucidForums.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Forum;

public interface IThreadViewService
{
    Task<ThreadView?> GetViewAsync(Guid threadId, CancellationToken ct = default);
}

public class ThreadViewService(LucidForums.Data.ApplicationDbContext db) : IThreadViewService
{
    public async Task<ThreadView?> GetViewAsync(Guid threadId, CancellationToken ct = default)
    {
        var thread = await db.Threads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId, ct);
        if (thread == null) return null;
        var messages = await db.Messages.AsNoTracking()
            .Where(m => m.ForumThreadId == threadId)
            .OrderBy(m => m.Path)
            .ToListAsync(ct);

        int GetDepth(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return 0;
            return path.Count(c => c == '.') + 1;
        }

        var msgViews = messages.Select(m => new MessageView(
            m.Id,
            m.ParentId,
            m.Content,
            m.CreatedById,
            m.CreatedAtUtc,
            GetDepth(m.Path),
            m.CharterScore
        )).ToList();

        var tags = (thread.Tags ?? new List<string>()).ToList();

        return new ThreadView(
            thread.Id,
            thread.Title,
            thread.ForumId,
            thread.CreatedById,
            thread.CreatedAtUtc,
            msgViews,
            thread.CharterScore,
            tags
        );
    }
}
