using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using mostlylucid.llmtranslate.Data;
using mostlylucid.llmtranslate.Helpers;
using mostlylucid.llmtranslate.Hubs;
using mostlylucid.llmtranslate.Models;

namespace mostlylucid.llmtranslate.Services;

internal sealed class PageLanguageSwitchService : IPageLanguageSwitchService
{
    private readonly ITranslationDbContext _db;
    private readonly ITranslationService _translationService;
    private readonly IHubContext<TranslationHub> _hubContext;
    private readonly IAiTranslationProvider _ai;
    private readonly ILogger<PageLanguageSwitchService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public PageLanguageSwitchService(
        ITranslationDbContext db,
        ITranslationService translationService,
        IHubContext<TranslationHub> hubContext,
        IAiTranslationProvider ai,
        ILogger<PageLanguageSwitchService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _translationService = translationService;
        _hubContext = hubContext;
        _ai = ai;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task<string> BuildSwitchResponseAsync(string languageCode, IEnumerable<string> keys, CancellationToken ct = default)
    {
        var html = new StringBuilder();

        var requested = keys.Distinct().ToList();
        if (requested.Count == 0)
        {
            html.AppendLine($"<span id=\"current-lang\" hx-swap-oob=\"innerHTML\">{languageCode.ToUpperInvariant()}</span>");
            return html.ToString();
        }

        var items = await _db.TranslationStrings
            .AsNoTracking()
            .Where(ts => requested.Contains(ts.Key))
            .Select(ts => new
            {
                ts.Id,
                ts.Key,
                ts.DefaultText,
                Translated = ts.Translations
                    .Where(t => t.LanguageCode == languageCode)
                    .Select(t => t.TranslatedText)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var missing = new List<(int Id, string Key, string DefaultText)>();

        foreach (var it in items)
        {
            var elementId = $"t-{ContentHash.Generate(it.Key)}";
            if (!string.IsNullOrEmpty(it.Translated))
            {
                html.AppendLine($"<span id=\"{elementId}\" hx-swap-oob=\"innerHTML\">{it.Translated}</span>");
            }
            else
            {
                missing.Add((it.Id, it.Key, it.DefaultText));
            }
        }

        // Always update indicator
        html.AppendLine($"<span id=\"current-lang\" hx-swap-oob=\"innerHTML\">{languageCode.ToUpperInvariant()}</span>");

        if (missing.Count > 0 && !string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase))
        {
            _ = Task.Run(() => BackgroundTranslateAsync(languageCode, missing), CancellationToken.None);
        }

        return html.ToString();
    }

    private async Task BackgroundTranslateAsync(string languageCode, List<(int Id, string Key, string DefaultText)> missing)
    {
        // Do NOT use the HTTP request CancellationToken or scoped services after the request ends.
        // Create a new scope and use a fresh DbContext and TranslationService instance.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ITranslationDbContext>();
        var translationService = scope.ServiceProvider.GetRequiredService<ITranslationService>();
        var ai = scope.ServiceProvider.GetRequiredService<IAiTranslationProvider>();

        var bgCt = CancellationToken.None; // prevent disposal-related cancellations

        // Progress tracking context
        var jobId = Guid.NewGuid().ToString("N");
        var total = missing.Count;
        var completed = 0;

        try
        {
            // Build batch and ask for JSON array response only
            var inputs = missing.ToDictionary(x => x.Key, x => x.DefaultText);

            // Broadcast initial progress (0%)
            await _hubContext.Clients.All.SendAsync("TranslationProgress", new
            {
                JobId = jobId,
                Total = total,
                Completed = completed,
                CurrentKey = (string?)null,
                Percentage = total > 0 ? 0 : 100
            }, bgCt);

            Dictionary<string, string> map;
            try
            {
                map = await ai.TranslateBatchAsync(inputs, languageCode, "en", bgCt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch translation failed, falling back to per-item translation");
                map = new Dictionary<string, string>();
            }

            foreach (var m in missing)
            {
                // Notify current key being processed
                await _hubContext.Clients.All.SendAsync("TranslationProgress", new
                {
                    JobId = jobId,
                    Total = total,
                    Completed = completed,
                    CurrentKey = m.Key,
                    Percentage = total > 0 ? (completed / (double)total) * 100.0 : 0
                }, bgCt);

                string translated;
                if (!map.TryGetValue(m.Key, out var translatedFromBatch) || string.IsNullOrWhiteSpace(translatedFromBatch))
                {
                    // Fallback per-item translate
                    translated = await ai.TranslateAsync(m.DefaultText, languageCode, "en", bgCt);
                }
                else
                {
                    translated = translatedFromBatch;
                }

                try
                {
                    var existing = await db.Translations
                        .Where(t => t.TranslationStringId == m.Id && t.LanguageCode == languageCode)
                        .FirstOrDefaultAsync(bgCt);

                    if (existing != null)
                    {
                        existing.TranslatedText = translated;
                        existing.UpdatedAtUtc = DateTime.UtcNow;
                        existing.Source = TranslationSource.AiGenerated;
                    }
                    else
                    {
                        db.Translations.Add(new Translation
                        {
                            TranslationStringId = m.Id,
                            LanguageCode = languageCode,
                            TranslatedText = translated,
                            Source = TranslationSource.AiGenerated,
                            AiModel = "Translation AI"
                        });
                    }

                    await db.SaveChangesAsync(bgCt);

                    // Broadcast the updated string
                    await _hubContext.Clients.All.SendAsync("StringTranslated", new
                    {
                        Key = m.Key,
                        LanguageCode = languageCode,
                        TranslatedText = translated
                    }, bgCt);

                    // Increment and broadcast progress
                    completed++;
                    await _hubContext.Clients.All.SendAsync("TranslationProgress", new
                    {
                        JobId = jobId,
                        Total = total,
                        Completed = completed,
                        CurrentKey = m.Key,
                        Percentage = total > 0 ? (completed / (double)total) * 100.0 : 100
                    }, bgCt);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upsert/broadcast translation for {Key} ({Lang})", m.Key, languageCode);
                }
            }

            // Broadcast completion
            await _hubContext.Clients.All.SendAsync("TranslationComplete", new
            {
                JobId = jobId,
                TranslatedCount = completed
            }, bgCt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch translation failed for {Lang}", languageCode);
        }
    }
}
