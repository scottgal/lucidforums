namespace LucidForums.Models.Entities;

/// <summary>
/// Persistent translation queue item stored in PostgreSQL
/// </summary>
public class TranslationQueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Content type (e.g., "Forum", "Thread", "Message")
    /// </summary>
    public string ContentType { get; set; } = null!;

    /// <summary>
    /// ID of the content to translate
    /// </summary>
    public string ContentId { get; set; } = null!;

    /// <summary>
    /// Field name to translate (e.g., "Title", "Content", "Description")
    /// </summary>
    public string FieldName { get; set; } = null!;

    /// <summary>
    /// The actual content to translate
    /// </summary>
    public string Content { get; set; } = null!;

    /// <summary>
    /// Source language code (default: "en")
    /// </summary>
    public string SourceLanguage { get; set; } = "en";

    /// <summary>
    /// Priority: higher numbers processed first (default: 0)
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Number of processing attempts
    /// </summary>
    public int AttemptCount { get; set; } = 0;

    /// <summary>
    /// Maximum number of retry attempts before giving up
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Last error message if processing failed
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// When this item was added to the queue
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this item was last processed (null if never processed)
    /// </summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>
    /// Whether this item has been successfully processed
    /// </summary>
    public bool IsProcessed { get; set; } = false;
}
