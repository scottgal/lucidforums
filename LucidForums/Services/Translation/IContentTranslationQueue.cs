using LucidForums.Models.Entities;

namespace LucidForums.Services.Translation;

public interface IContentTranslationQueue
{
    /// <summary>
    /// Queue a message for translation into all available languages
    /// </summary>
    void QueueMessageTranslation(Guid messageId, string content);

    /// <summary>
    /// Queue a specific field of content for translation
    /// </summary>
    void QueueContentTranslation(string contentType, string contentId, string fieldName, string content, string sourceLanguage = "en", int priority = 0);

    /// <summary>
    /// Dequeue the next item from the persistent queue (highest priority first, oldest first)
    /// </summary>
    Task<TranslationQueueItem?> DequeueAsync(CancellationToken ct = default);

    /// <summary>
    /// Mark an item as processed successfully
    /// </summary>
    Task MarkProcessedAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Mark an item as failed (increment attempt count)
    /// </summary>
    Task MarkFailedAsync(Guid itemId, string error, CancellationToken ct = default);

    /// <summary>
    /// Get count of pending items in queue
    /// </summary>
    Task<int> GetPendingCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Clean up old processed items (keep last 7 days)
    /// </summary>
    Task CleanupOldItemsAsync(int daysToKeep = 7, CancellationToken ct = default);
}
