using Microsoft.AspNetCore.Identity;

namespace LucidForums.Models.Entities;

public class User : IdentityUser
{
    public ICollection<ForumUser> ForumMemberships { get; set; } = new List<ForumUser>();
    public ICollection<ForumThread> ThreadsCreated { get; set; } = new List<ForumThread>();
    public ICollection<Message> MessagesCreated { get; set; } = new List<Message>();
}