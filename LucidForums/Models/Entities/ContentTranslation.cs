using System.ComponentModel.DataAnnotations;

namespace LucidForums.Models.Entities;

/// <summary>
/// Represents translations of user-generated content (forums, threads, messages)
/// Uses a polymorphic approach with ContentType + ContentId
/// </summary>
public class ContentTranslation
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Type of content (e.g., "Forum", "Thread", "Message")
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// ID of the content being translated (stored as string to support different ID types)
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// Field being translated (e.g., "Title", "Description", "Content")
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Language code (ISO 639-1)
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// The translated text
    /// </summary>
    [Required]
    public string TranslatedText { get; set; } = string.Empty;

    /// <summary>
    /// Hash of the source text to detect when source content changes
    /// </summary>
    [MaxLength(64)]
    public string? SourceHash { get; set; }

    /// <summary>
    /// Whether this translation is stale (source content changed)
    /// </summary>
    public bool IsStale { get; set; }

    public TranslationSource Source { get; set; } = TranslationSource.AiGenerated;

    [MaxLength(100)]
    public string? AiModel { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
