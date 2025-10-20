using LucidForums.Models.Entities;

namespace LucidForums.Services.Moderation;

public interface IModerationService
{
    Task<ModerationResult> EvaluatePostAsync(Charter charter, string content, string? model = null, CancellationToken ct = default);
}