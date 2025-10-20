namespace LucidForums.Models.Entities;

// Forum membership linking a User to a Forum with a role
public class ForumUser
{
    public Guid ForumId { get; set; }
    public Forum Forum { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;

    public string Role { get; set; } = "member"; // member, moderator, admin

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}