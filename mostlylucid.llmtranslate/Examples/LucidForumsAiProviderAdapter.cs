/*
 * EXAMPLE IMPLEMENTATION
 *
 * This file shows how to adapt LucidForums' ITextAiService to work with
 * the AutoTranslate package's IAiTranslationProvider interface.
 *
 * Copy this to your LucidForums project and register it in your DI container.
 */

using System.Text.Json;
using LucidForums.AutoTranslate.Services;

namespace LucidForums.Examples;

/// <summary>
/// Adapter that bridges LucidForums' ITextAiService to IAiTranslationProvider
/// This allows the AutoTranslate package to use LucidForums' existing AI infrastructure
/// </summary>
public class LucidForumsAiProviderAdapter : IAiTranslationProvider
{
    private readonly ITextAiService _aiService;
    private readonly ILogger<LucidForumsAiProviderAdapter> _logger;

    public LucidForumsAiProviderAdapter(
        ITextAiService aiService,
        ILogger<LucidForumsAiProviderAdapter> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<string> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = "en",
        CancellationToken ct = default)
    {
        try
        {
            // Use LucidForums' existing translation method
            return await _aiService.TranslateAsync(text, targetLanguage, sourceLanguage, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to translate text to {Language}", targetLanguage);
            return text; // Fallback to original text
        }
    }

    public async Task<Dictionary<string, string>> TranslateBatchAsync(
        Dictionary<string, string> items,
        string targetLanguage,
        string? sourceLanguage = "en",
        CancellationToken ct = default)
    {
        try
        {
            // Build a batch translation prompt using LucidForums' Charter system
            var charter = new Charter
            {
                Name = "BatchTranslation",
                Purpose = $"Translate UI strings from {sourceLanguage ?? "en"} to {targetLanguage} accurately and naturally"
            };

            var jsonPayload = JsonSerializer.Serialize(
                items.Select(kvp => new { key = kvp.Key, text = kvp.Value }).ToList()
            );

            var prompt = $@"Translate the following UI strings from {sourceLanguage ?? "en"} to {targetLanguage}.
Preserve HTML tags, entities, and placeholders like {{0}} or {{name}}. Do not add explanations.
Respond with ONLY a JSON array of objects with properties: key, translated. Do not wrap in code fences.

Input items (JSON array of {{ key, text }}):
{jsonPayload}";

            var aiResult = await _aiService.GenerateAsync(charter, prompt, ct: ct);

            // Parse the JSON response
            return ParseBatchResponse(aiResult, items);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch translation failed, falling back to individual translations");

            // Fallback: translate one by one
            var result = new Dictionary<string, string>();
            foreach (var (key, text) in items)
            {
                try
                {
                    var translated = await _aiService.TranslateAsync(text, targetLanguage, sourceLanguage, ct);
                    result[key] = translated;
                }
                catch
                {
                    result[key] = text; // Fallback to original
                }
            }
            return result;
        }
    }

    private Dictionary<string, string> ParseBatchResponse(string? aiResult, Dictionary<string, string> fallback)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(aiResult))
            return result;

        try
        {
            // Try direct JSON parse
            var items = JsonSerializer.Deserialize<List<BatchItem>>(aiResult, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item.Key))
                        result[item.Key] = item.Translated ?? string.Empty;
                }
                return result;
            }
        }
        catch
        {
            // If direct parse fails, try extracting JSON array from text
            var extracted = ExtractFirstJsonArray(aiResult);
            if (extracted != null)
            {
                try
                {
                    var items = JsonSerializer.Deserialize<List<BatchItem>>(extracted, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            if (!string.IsNullOrWhiteSpace(item.Key))
                                result[item.Key] = item.Translated ?? string.Empty;
                        }
                    }
                }
                catch
                {
                    // Fall through to empty result
                }
            }
        }

        return result;
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

// Note: This assumes you have access to these types from LucidForums:
// - ITextAiService
// - Charter

// If not, you'll need to reference the LucidForums project or adjust the implementation.
