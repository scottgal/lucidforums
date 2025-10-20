namespace LucidForums.Models.Dtos;

public record MessageView(Guid Id, Guid? ParentId, string Content, string? AuthorId, DateTime CreatedAtUtc, int Depth);

public record ThreadView(Guid Id, string Title, Guid ForumId, string? AuthorId, DateTime CreatedAtUtc, IReadOnlyList<MessageView> Messages);
