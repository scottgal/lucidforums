using LucidForums.Data;
using LucidForums.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace LucidForums.Services.Translation;

/// <summary>
/// PostgreSQL-backed persistent queue for background content translation.
/// Survives application restarts and supports priority-based processing.
/// </summary>
public class ContentTranslationQueue : IContentTranslationQueue
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<ContentTranslationQueue> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ContentTranslationQueue(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ILogger<ContentTranslationQueue> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public void QueueMessageTranslation(Guid messageId, string content)
    {
        QueueContentTranslation("Message", messageId.ToString(), "Content", content);
    }

    public void QueueContentTranslation(string contentType, string contentId, string fieldName, string content, string sourceLanguage = "en", int priority = 0)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _lock.WaitAsync();
                try
                {
                    await using var db = await _dbFactory.CreateDbContextAsync();

                    // Check if this exact item is already in the queue (unprocessed)
                    var exists = await db.TranslationQueue
                        .AnyAsync(q =>
                            q.ContentType == contentType &&
                            q.ContentId == contentId &&
                            q.FieldName == fieldName &&
                            !q.IsProcessed);

                    if (exists)
                    {
                        _logger.LogDebug("Translation for {ContentType}:{ContentId}.{FieldName} already queued",
                            contentType, contentId, fieldName);
                        return;
                    }

                    var item = new TranslationQueueItem
                    {
                        ContentType = contentType,
                        ContentId = contentId,
                        FieldName = fieldName,
                        Content = content,
                        SourceLanguage = sourceLanguage,
                        Priority = priority,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    db.TranslationQueue.Add(item);
                    await db.SaveChangesAsync();

                    _logger.LogDebug("Queued translation for {ContentType}:{ContentId}.{FieldName} (priority: {Priority})",
                        contentType, contentId, fieldName, priority);
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue translation for {ContentType}:{ContentId}.{FieldName}",
                    contentType, contentId, fieldName);
            }
        });
    }

    /// <summary>
    /// Dequeue the next item from the persistent queue (highest priority first, oldest first)
    /// </summary>
    public async Task<TranslationQueueItem?> DequeueAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var item = await db.TranslationQueue
                .Where(q => !q.IsProcessed && q.AttemptCount < q.MaxAttempts)
                .OrderByDescending(q => q.Priority)
                .ThenBy(q => q.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);

            return item;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Mark an item as processed successfully
    /// </summary>
    public async Task MarkProcessedAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var item = await db.TranslationQueue.FindAsync(new object[] { itemId }, ct);
        if (item != null)
        {
            item.IsProcessed = true;
            item.ProcessedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Mark an item as failed (increment attempt count)
    /// </summary>
    public async Task MarkFailedAsync(Guid itemId, string error, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var item = await db.TranslationQueue.FindAsync(new object[] { itemId }, ct);
        if (item != null)
        {
            item.AttemptCount++;
            item.LastError = error;
            item.ProcessedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Get count of pending items in queue
    /// </summary>
    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TranslationQueue
            .CountAsync(q => !q.IsProcessed && q.AttemptCount < q.MaxAttempts, ct);
    }

    /// <summary>
    /// Clean up old processed items (keep last 7 days)
    /// </summary>
    public async Task CleanupOldItemsAsync(int daysToKeep = 7, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-daysToKeep);
        var oldItems = await db.TranslationQueue
            .Where(q => q.IsProcessed && q.ProcessedAtUtc < cutoff)
            .ToListAsync(ct);

        if (oldItems.Any())
        {
            db.TranslationQueue.RemoveRange(oldItems);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Cleaned up {Count} old translation queue items", oldItems.Count);
        }
    }
}
