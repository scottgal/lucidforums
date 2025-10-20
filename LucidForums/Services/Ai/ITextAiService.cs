using System;
using System.Threading;
using System.Threading.Tasks;
using LucidForums.Models.Entities;

namespace LucidForums.Services.Ai;

public interface ITextAiService
{
    /// <summary>
    /// Generate assistant text given a charter (as system prompt) and user input.
    /// </summary>
    Task<string> GenerateAsync(Charter charter, string userInput, string? model = null, CancellationToken ct = default);

    /// <summary>
    /// Translate plain text into the target language. Should preserve basic formatting.
    /// </summary>
    Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default);

    /// <summary>
    /// Stream a translation by invoking onChunk with pieces of the translated text as they become available.
    /// Implementations may fall back to chunking a full translation if streaming is not supported.
    /// </summary>
    Task TranslateStreamAsync(string text, string targetLanguage, Func<string, Task> onChunk, CancellationToken ct = default);
}