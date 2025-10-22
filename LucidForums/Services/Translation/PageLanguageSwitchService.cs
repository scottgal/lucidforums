using System.Text;
using System.Text.Json;
using LucidForums.Data;
using LucidForums.Helpers;
using LucidForums.Hubs;
using LucidForums.Models.Entities;
using LucidForums.Services.Ai;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace LucidForums.Services.Translation;

internal sealed class PageLanguageSwitchService : IPageLanguageSwitchService
{
    private readonly ApplicationDbContext _db;
    private readonly ITranslationService _translationService;
    private readonly IHubContext<TranslationHub> _hubContext;
    private readonly ITextAiService _ai;
    private readonly ILogger<PageLanguageSwitchService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public PageLanguageSwitchService(
        ApplicationDbContext db,
        ITranslationService translationService,
        IHubContext<TranslationHub> hubContext,
        ITextAiService ai,
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
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var translationService = scope.ServiceProvider.GetRequiredService<ITranslationService>();

        var bgCt = CancellationToken.None; // prevent disposal-related cancellations

        try
        {
            // Build batch and ask for JSON array response only
            var inputs = missing.Select(x => new { key = x.Key, text = x.DefaultText }).ToList();
            var jsonPayload = JsonSerializer.Serialize(inputs);

            var charter = new Charter
            {
                Name = "BatchTranslation",
                Purpose = $"Translate UI strings from en to {languageCode} accurately and naturally"
            };

            var prompt = $@"Translate the following UI strings from English (en) to {languageCode}.
Preserve HTML tags, entities, and placeholders like {{0}} or {{name}}. Do not add explanations.
Respond with ONLY a JSON array of objects with properties: key, translated. Do not wrap in code fences.
Input items (JSON array of {{ key, text }}):
{jsonPayload}";

            var aiResult = await _ai.GenerateAsync(charter, prompt, ct: bgCt);
            var map = ParseBatchToMap(aiResult);

            foreach (var m in missing)
            {
                if (!map.TryGetValue(m.Key, out var translated) || string.IsNullOrWhiteSpace(translated))
                {
                    // Fallback per-item translate
                    translated = await translationService.TranslateAsync(m.DefaultText, languageCode, "en", bgCt);
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
                        db.Translations.Add(new Models.Entities.Translation
                        {
                            TranslationStringId = m.Id,
                            LanguageCode = languageCode,
                            TranslatedText = translated,
                            Source = TranslationSource.AiGenerated,
                            AiModel = "Translation AI"
                        });
                    }

                    await db.SaveChangesAsync(bgCt);

                    await _hubContext.Clients.All.SendAsync("StringTranslated", new
                    {
                        Key = m.Key,
                        LanguageCode = languageCode,
                        TranslatedText = translated
                    }, bgCt);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upsert/broadcast translation for {Key} ({Lang})", m.Key, languageCode);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch translation failed for {Lang}; falling back per-item inside loop where possible", languageCode);
        }
    }

    private static Dictionary<string, string> ParseBatchToMap(string? aiResult)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(aiResult)) return map;

        // Try direct parse first
        if (TryDeserialize(aiResult, out var items))
        {
            foreach (var it in items!) if (!string.IsNullOrWhiteSpace(it.Key)) map[it.Key!] = it.Translated ?? string.Empty;
            return map;
        }

        // Extract first JSON array from the text (handles code fences or prose around JSON)
        var extracted = ExtractFirstJsonArray(aiResult);
        if (extracted != null && TryDeserialize(extracted, out items))
        {
            foreach (var it in items!) if (!string.IsNullOrWhiteSpace(it.Key)) map[it.Key!] = it.Translated ?? string.Empty;
            return map;
        }

        return map;
    }

    private static bool TryDeserialize(string json, out List<BatchItem>? items)
    {
        try
        {
            items = JsonSerializer.Deserialize<List<BatchItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return items != null;
        }
        catch
        {
            items = null;
            return false;
        }
    }

    private static string? ExtractFirstJsonArray(string text)
    {
        int start = -1, depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '[')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (text[i] == ']')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    return text.Substring(start, i - start + 1);
                }
            }
        }
        return null;
    }

    private sealed class BatchItem
    {
        public string? Key { get; set; }
        public string? Translated { get; set; }
    }
}
