using System.Text.Json.Serialization;

namespace LucidForums.Services.Seeding;

public record ForumSeedingRequest(
    Guid JobId,
    string ForumName,
    string ForumSlug,
    string? Description,
    int ThreadCount,
    int RepliesPerThread
);

public record ForumSeedingProgress(
    Guid JobId,
    string Stage,
    string Message,
    string? EntityId = null
)
{
    [JsonIgnore]
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
