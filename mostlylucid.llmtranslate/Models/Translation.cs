using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mostlylucid.llmtranslate.Models;

/// <summary>
/// Represents a translation of a string in a specific language
/// </summary>
public class Translation
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to TranslationString
    /// </summary>
    public int TranslationStringId { get; set; }

    /// <summary>
    /// Language code (ISO 639-1, e.g., "en", "es", "fr", "de", "ja")
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
    /// How this translation was created
    /// </summary>
    public TranslationSource Source { get; set; } = TranslationSource.Manual;

    /// <summary>
    /// AI model used if auto-translated
    /// </summary>
    [MaxLength(100)]
    public string? AiModel { get; set; }

    /// <summary>
    /// Whether this translation has been reviewed/approved by a human
    /// </summary>
    public bool IsApproved { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation property
    [ForeignKey(nameof(TranslationStringId))]
    public TranslationString? TranslationString { get; set; }
}

public enum TranslationSource
{
    Manual = 0,
    AiGenerated = 1,
    Imported = 2
}
