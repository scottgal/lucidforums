using LucidForums.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LucidForums.Services.Admin;

public interface IAdminMaintenanceService
{
    Task ClearContentAsync(CancellationToken ct = default);
    Task<int> IndexAllMessagesAsync(CancellationToken ct = default);
    Task<(int Total, int Indexed)> GetIndexingStatusAsync(CancellationToken ct = default);
}

public class AdminMaintenanceService(
    ApplicationDbContext db,
    ILogger<AdminMaintenanceService> logger,
    LucidForums.Services.Search.IEmbeddingService embeddingService) : IAdminMaintenanceService
{
    public async Task<int> IndexAllMessagesAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Starting batch indexing of all messages...");

        // Get all message IDs
        var messageIds = await db.Messages
            .AsNoTracking()
            .Select(m => m.Id)
            .ToListAsync(ct);

        logger.LogInformation("Found {Count} messages to index", messageIds.Count);

        var indexed = 0;
        var batchSize = 10; // Process in small batches to avoid overwhelming the system

        for (int i = 0; i < messageIds.Count; i += batchSize)
        {
            var batch = messageIds.Skip(i).Take(batchSize).ToList();
            var tasks = batch.Select(async id =>
            {
                try
                {
                    await embeddingService.IndexMessageAsync(id, ct);
                    return true;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to index message {MessageId}", id);
                    return false;
                }
            });

            var results = await Task.WhenAll(tasks);
            indexed += results.Count(r => r);

            logger.LogInformation("Indexed {Indexed}/{Total} messages", indexed, messageIds.Count);

            // Small delay to avoid overwhelming the embedding service
            if (i + batchSize < messageIds.Count)
            {
                await Task.Delay(100, ct);
            }
        }

        logger.LogInformation("Batch indexing completed. Indexed {Indexed}/{Total} messages", indexed, messageIds.Count);
        return indexed;
    }

    public async Task<(int Total, int Indexed)> GetIndexingStatusAsync(CancellationToken ct = default)
    {
        var totalMessages = await db.Messages.CountAsync(ct);

        int indexedMessages;
        try
        {
            // Count rows in message_embeddings table
            var result = await db.Database.ExecuteSqlRawAsync(
                "SELECT COUNT(*) FROM message_embeddings",
                ct
            );

            // ExecuteSqlRawAsync returns rows affected, not the count
            // We need to query differently
            using var connection = db.Database.GetDbConnection();
            await connection.OpenAsync(ct);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM message_embeddings";
            var countObj = await command.ExecuteScalarAsync(ct);
            indexedMessages = Convert.ToInt32(countObj);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get indexed message count");
            indexedMessages = 0;
        }

        return (totalMessages, indexedMessages);
    }

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
