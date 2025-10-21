using LucidForums.Models.Entities;

namespace LucidForums.Services.Search;

public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task IndexMessageAsync(Guid messageId, CancellationToken ct = default);
    Task<List<(Guid MessageId, double Score)>> SearchAsync(string query, Guid? forumId, int limit = 10, CancellationToken ct = default);
}
