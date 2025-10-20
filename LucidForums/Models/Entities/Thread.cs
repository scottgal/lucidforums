namespace LucidForums.Models.Entities;

public class ForumThread
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ForumId { get; set; }
    public Forum Forum { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    // Root message of the thread
    public Guid RootMessageId { get; set; }
    public Message RootMessage { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
