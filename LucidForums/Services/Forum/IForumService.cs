using LucidForums.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Forum;

public interface IForumService
{
    Task<LucidForums.Models.Entities.Forum?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<LucidForums.Models.Entities.Forum> CreateAsync(string name, string slug, string? description, string? createdById, CancellationToken ct = default);
    Task<List<LucidForums.Models.Entities.Forum>> ListAsync(int skip = 0, int take = 50, CancellationToken ct = default);
}

public class ForumService(LucidForums.Data.ApplicationDbContext db) : IForumService
{
    public async Task<LucidForums.Models.Entities.Forum?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        return await db.Forums.Include(f => f.Charter).FirstOrDefaultAsync(f => f.Slug == slug, ct);
    }

    public async Task<LucidForums.Models.Entities.Forum> CreateAsync(string name, string slug, string? description, string? createdById, CancellationToken ct = default)
    {
        // Validate creator exists; otherwise, leave null to avoid FK violations
        string? validCreatorId = null;
        if (!string.IsNullOrWhiteSpace(createdById))
        {
            try
            {
                bool exists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == createdById, ct);
                if (exists) validCreatorId = createdById;
            }
            catch
            {
                // In case of provider limitations or errors, fallback to null
            }
        }

        var forum = new LucidForums.Models.Entities.Forum
        {
            Name = name,
            Slug = slug,
            Description = description,
            CreatedById = validCreatorId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Forums.Add(forum);
        await db.SaveChangesAsync(ct);
        return forum;
    }

    public Task<List<LucidForums.Models.Entities.Forum>> ListAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        return db.Forums.OrderByDescending(f => f.CreatedAtUtc).Skip(skip).Take(take).ToListAsync(ct);
    }
}
