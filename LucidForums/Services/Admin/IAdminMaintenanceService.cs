using LucidForums.Data;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Admin;

public interface IAdminMaintenanceService
{
    Task ClearContentAsync(CancellationToken ct = default);
}

public class AdminMaintenanceService(ApplicationDbContext db) : IAdminMaintenanceService
{
    public async Task ClearContentAsync(CancellationToken ct = default)
    {
        // Use a transaction to ensure consistency
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Remove forum memberships first
            db.ForumUsers.RemoveRange(db.ForumUsers);
            await db.SaveChangesAsync(ct);

            // Remove forums (cascades to threads and messages)
            db.Forums.RemoveRange(db.Forums);
            await db.SaveChangesAsync(ct);

            // Clear message embeddings table if it exists
            try
            {
                // Use plain DELETE to be portable; TRUNCATE may require special privileges
                await db.Database.ExecuteSqlRawAsync("DELETE FROM message_embeddings", ct);
            }
            catch
            {
                // Ignore if table does not exist or provider is not PostgreSQL/SQLite
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
