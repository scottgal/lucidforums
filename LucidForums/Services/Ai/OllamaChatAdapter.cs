using System.Threading;
using System.Threading.Tasks;
using LucidForums.Models.Entities;
using LucidForums.Services.Llm;

namespace LucidForums.Services.Ai;

/// <summary>
/// Backwards-compatibility adapter so existing code depending on IOllamaChatService keeps working.
/// Internally routes to ITextAiService.
/// </summary>
public class OllamaChatAdapter(ITextAiService textAiService) : IOllamaChatService
{
    public Task<string> ChatAsync(Charter charter, string userInput, string? model = null, CancellationToken ct = default)
        => textAiService.GenerateAsync(charter, userInput, model, ct);
}