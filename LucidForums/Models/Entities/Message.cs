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

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
}