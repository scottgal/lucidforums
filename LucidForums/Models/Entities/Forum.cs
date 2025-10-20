namespace LucidForums.Models.Entities;

public class Forum
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Guid? CharterId { get; set; }
    public Charter? Charter { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    public ICollection<ForumThread> Threads { get; set; } = new List<ForumThread>();
}