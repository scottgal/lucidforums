using System.Collections.Generic;

namespace LucidForums.Services.Ai;

public interface IAiSettingsService
{
    // Provider names are like "ollama", "lmstudio", etc.
    string? GenerationProvider { get; set; }
    string? GenerationModel { get; set; }

    string? TranslationProvider { get; set; }
    string? TranslationModel { get; set; }

    string? EmbeddingProvider { get; set; }
    string? EmbeddingModel { get; set; }

    // Helpers to expose all provider names (populated externally by caller/UI)
    IReadOnlyList<string> KnownProviders { get; }
    void SetKnownProviders(IEnumerable<string> providers);
}