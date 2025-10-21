using LucidForums.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LucidForums.Services.Admin;

public interface IAdminMaintenanceService
{
    Task ClearContentAsync(CancellationToken ct = default);
}

public class AdminMaintenanceService(ApplicationDbContext db, ILogger<AdminMaintenanceService> logger) : IAdminMaintenanceService
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

            // Remove messages and threads explicitly to ensure any orphaned rows are removed
            db.Messages.RemoveRange(db.Messages);
            await db.SaveChangesAsync(ct);

            db.Threads.RemoveRange(db.Threads);
            await db.SaveChangesAsync(ct);

            // Remove forums (cascades as a final safeguard)
            db.Forums.RemoveRange(db.Forums);
            await db.SaveChangesAsync(ct);

            // Clear message embeddings table if it exists
            try
            {
                // Use DELETE which is portable; some providers may require schema-qualified names
                await db.Database.ExecuteSqlRawAsync("DELETE FROM message_embeddings", ct);
            }
            catch (Exception ex)
            {
                // Log a warning so the operator can investigate; don't fail the whole clear operation
                logger.LogWarning(ex, "Failed to delete from message_embeddings table (may not exist or SQL not supported by provider).");
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
