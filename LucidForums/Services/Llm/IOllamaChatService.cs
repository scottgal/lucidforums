using LucidForums.Models.Entities;

namespace LucidForums.Services.Llm;

public interface IOllamaChatService
{
    Task<string> ChatAsync(Charter charter, string userInput, string? model = null, CancellationToken ct = default);
}