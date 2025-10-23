namespace LucidForums.Models.Entities;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ForumThreadId { get; set; }
    public ForumThread Thread { get; set; } = null!;

    public Guid? ParentId { get; set; }
    public Message? Parent { get; set; }
    public ICollection<Message> Replies { get; set; } = new List<Message>();

    // Path for hierarchical threading; map to PostgreSQL ltree
    public string? Path { get; set; }

    public string Content { get; set; } = string.Empty;

    // Score (0-100) indicating how well this message aligns with the forum's charter; null if not evaluated
    public double? CharterScore { get; set; }

    /// <summary>
    /// Source language of the message content (ISO 639-1 code, e.g., "en", "es", "fr")
    /// </summary>
    public string SourceLanguage { get; set; } = "en";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
}