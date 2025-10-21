using System.Collections.Generic;
using LucidForums.Data;
using LucidForums.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LucidForums.Services.Ai;

public class AiSettingsService : IAiSettingsService
{
    private readonly object _lock = new();
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public AiSettingsService(
        IOptionsMonitor<AiOptions> aiOptions,
        IOptionsMonitor<LucidForums.Services.Search.EmbeddingOptions> embOptions,
        IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;

        // Load persisted settings if present; otherwise seed from configuration
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var row = db.AppSettings.FirstOrDefault() ?? new AppSettings { Id = 1 };
            if (row.Id == 0) row.Id = 1;

            var ai = aiOptions.CurrentValue;
            var emb = embOptions.CurrentValue;

            GenerationProvider = row.GenerationProvider ?? ai.Provider;
            GenerationModel = row.GenerationModel ?? ai.DefaultModel;
            TranslationProvider = row.TranslationProvider ?? ai.Provider;
            TranslationModel = row.TranslationModel ?? ai.DefaultModel;
            EmbeddingProvider = row.EmbeddingProvider ?? emb.Provider;
            EmbeddingModel = row.EmbeddingModel ?? emb.Model;

            // If no row existed, create it with the initial values
            if (db.AppSettings.Any() == false)
            {
                db.AppSettings.Add(new AppSettings
                {
                    Id = 1,
                    GenerationProvider = GenerationProvider,
                    GenerationModel = GenerationModel,
                    TranslationProvider = TranslationProvider,
                    TranslationModel = TranslationModel,
                    EmbeddingProvider = EmbeddingProvider,
                    EmbeddingModel = EmbeddingModel
                });
                db.SaveChanges();
            }
        }
        catch
        {
            // Fallback to config-only if DB not available at startup
            var ai = aiOptions.CurrentValue;
            var emb = embOptions.CurrentValue;
            GenerationProvider = ai.Provider;
            GenerationModel = ai.DefaultModel;
            TranslationProvider = ai.Provider;
            TranslationModel = ai.DefaultModel;
            EmbeddingProvider = emb.Provider;
            EmbeddingModel = emb.Model;
        }
    }

    private void Persist()
    {
        lock (_lock)
        {
            try
            {
                using var db = _dbFactory.CreateDbContext();
                var row = db.AppSettings.FirstOrDefault(x => x.Id == 1);
                if (row == null)
                {
                    row = new AppSettings { Id = 1 };
                    db.AppSettings.Add(row);
                }
                row.GenerationProvider = GenerationProvider;
                row.GenerationModel = GenerationModel;
                row.TranslationProvider = TranslationProvider;
                row.TranslationModel = TranslationModel;
                row.EmbeddingProvider = EmbeddingProvider;
                row.EmbeddingModel = EmbeddingModel;
                db.SaveChanges();
            }
            catch
            {
                // ignore persistence errors at runtime
            }
        }
    }

    private string? _generationProvider;
    public string? GenerationProvider
    {
        get => _generationProvider;
        set { _generationProvider = value; Persist(); }
    }

    private string? _generationModel;
    public string? GenerationModel
    {
        get => _generationModel;
        set { _generationModel = value; Persist(); }
    }

    private string? _translationProvider;
    public string? TranslationProvider
    {
        get => _translationProvider;
        set { _translationProvider = value; Persist(); }
    }

    private string? _translationModel;
    public string? TranslationModel
    {
        get => _translationModel;
        set { _translationModel = value; Persist(); }
    }

    private string? _embeddingProvider;
    public string? EmbeddingProvider
    {
        get => _embeddingProvider;
        set { _embeddingProvider = value; Persist(); }
    }

    private string? _embeddingModel;
    public string? EmbeddingModel
    {
        get => _embeddingModel;
        set { _embeddingModel = value; Persist(); }
    }

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