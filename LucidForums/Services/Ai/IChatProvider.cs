using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LucidForums.Models.Entities;

namespace LucidForums.Services.Ai;

public interface IChatProvider
{
    string Name { get; } // e.g., "ollama", "lmstudio", "openai"

    Task<string> GenerateAsync(Charter charter, string userInput, string? model, double? temperature, int? maxTokens, CancellationToken ct);

    Task<string> TranslateAsync(string text, string targetLanguage, string? model, double? temperature, int? maxTokens, CancellationToken ct);

    Task TranslateStreamAsync(string text, string targetLanguage, string? model, double? temperature, int? maxTokens, Func<string, Task> onChunk, CancellationToken ct);

    /// <summary>
    /// Returns a list of available model identifiers for this provider.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);
}
