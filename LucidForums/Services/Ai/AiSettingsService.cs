using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace LucidForums.Services.Ai;

public class AiSettingsService : IAiSettingsService
{
    private readonly object _lock = new();

    public AiSettingsService(IOptionsMonitor<AiOptions> aiOptions, IOptionsMonitor<LucidForums.Services.Search.EmbeddingOptions> embOptions)
    {
        var ai = aiOptions.CurrentValue;
        var emb = embOptions.CurrentValue;
        GenerationProvider = ai.Provider;
        GenerationModel = ai.DefaultModel;
        TranslationProvider = ai.Provider;
        TranslationModel = ai.DefaultModel;
        EmbeddingProvider = emb.Provider;
        EmbeddingModel = emb.Model;
    }

    public string? GenerationProvider { get; set; }
    public string? GenerationModel { get; set; }
    public string? TranslationProvider { get; set; }
    public string? TranslationModel { get; set; }
    public string? EmbeddingProvider { get; set; }
    public string? EmbeddingModel { get; set; }

    private IReadOnlyList<string> _knownProviders = new List<string>();
    public IReadOnlyList<string> KnownProviders => _knownProviders;

    public void SetKnownProviders(IEnumerable<string> providers)
    {
        lock (_lock)
        {
            _knownProviders = providers is List<string> list ? list : new List<string>(providers);
        }
    }
}