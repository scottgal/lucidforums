namespace LucidForums.Models.ViewModels;

public record MessageVm(Guid Id, Guid? ParentId, string Content, string? AuthorId, DateTime CreatedAtUtc, int Depth);

public record ThreadVm(Guid Id, string Title, Guid ForumId, string? AuthorId, DateTime CreatedAtUtc, IReadOnlyList<MessageVm> Messages);

public class CreateThreadVm
{
    public Guid ForumId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class ReplyVm
{
    public Guid ThreadId { get; set; }
    public Guid? ParentId { get; set; }
    public string Content { get; set; } = string.Empty;
}
