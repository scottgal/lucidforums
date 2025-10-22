using System.ComponentModel.DataAnnotations;

namespace mostlylucid.llmtranslate.Models;

/// <summary>
/// Represents a translatable UI string with a key and default text
/// </summary>
public class TranslationString
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Unique key for this string (e.g., "home.welcome.title")
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The default text in the base language (English)
    /// </summary>
    [Required]
    public string DefaultText { get; set; } = string.Empty;

    /// <summary>
    /// Optional context/description to help translators
    /// </summary>
    [MaxLength(500)]
    public string? Context { get; set; }

    /// <summary>
    /// Category for organizing strings (e.g., "Home", "Admin", "Forum")
    /// </summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation property
    public ICollection<Translation> Translations { get; set; } = new List<Translation>();
}
