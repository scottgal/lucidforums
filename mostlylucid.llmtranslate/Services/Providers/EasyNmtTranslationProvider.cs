using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace mostlylucid.llmtranslate.Services.Providers;

/// <summary>
/// IAiTranslationProvider implementation that talks to an EasyNMT REST server.
/// Expected EasyNMT server endpoints (FastAPI reference):
///  - POST /translate { "text": string or string[], "source_lang": string|null, "target_lang": string }
///  - Response: { "translations": string[] } or { "translation": string }
/// Some community images also expose GET /translate?text=&source_lang=&target_lang=. We primarily use POST JSON.
/// Limitations:
///  - Many EasyNMT servers do not support batch input; we will fallback to per-item translation.
///  - Language codes should be ISO 639-1 (e.g., en, de, fr). Some backends require full names; we pass through as-is.
/// </summary>
public class EasyNmtTranslationProvider : IAiTranslationProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<EasyNmtTranslationProvider> _logger;

    /// <param name="baseUrl">Base URL of the EasyNMT server, e.g. http://localhost:8080/</param>
    public EasyNmtTranslationProvider(HttpClient httpClient, ILogger<EasyNmtTranslationProvider> logger, string? baseUrl = null)
    {
        _http = httpClient;
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            _http.BaseAddress = new Uri(baseUrl);
        }
        if (_http.BaseAddress == null)
        {
            _http.BaseAddress = new Uri("http://localhost:24080/");
        }
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = "en", CancellationToken ct = default)
    {
        try
        {
            // Prefer POST JSON API
            var payload = new
            {
                text = text,
                target_lang = targetLanguage,
                source_lang = string.IsNullOrWhiteSpace(sourceLanguage) ? null : sourceLanguage
            };

            using var resp = await _http.PostAsJsonAsync("translate", payload, ct);

            // Some deployments might be at /api/translate
            if (!resp.IsSuccessStatusCode)
            {
                // Log the error details before trying alternative endpoint
                var errorContent = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("EasyNMT /translate returned {StatusCode}: {Error}", resp.StatusCode, errorContent);

                resp.Dispose();
                using var respAlt = await _http.PostAsJsonAsync("api/translate", payload, ct);
                if (!respAlt.IsSuccessStatusCode)
                {
                    var altErrorContent = await respAlt.Content.ReadAsStringAsync(ct);
                    _logger.LogError("EasyNMT /api/translate also failed with {StatusCode}: {Error}", respAlt.StatusCode, altErrorContent);

                    // Return original text instead of throwing
                    _logger.LogWarning("Returning original text due to EasyNMT failure");
                    return text;
                }
                return await ReadSingleTranslationAsync(respAlt, text, ct);
            }

            return await ReadSingleTranslationAsync(resp, text, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EasyNMT translation failed for target language {TargetLang}", targetLanguage);
            return text; // fail soft: return original
        }
    }

    public async Task<Dictionary<string, string>> TranslateBatchAsync(Dictionary<string, string> items, string targetLanguage, string? sourceLanguage = "en", CancellationToken ct = default)
    {
        // Try batch where server supports array input; otherwise fallback to per-item
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (items.Count == 0) return result;
        try
        {
            var ordered = items.ToList();
            var texts = ordered.Select(kvp => kvp.Value).ToArray();
            var payload = new
            {
                text = texts,
                target_lang = targetLanguage,
                source_lang = string.IsNullOrWhiteSpace(sourceLanguage) ? null : sourceLanguage
            };

            using var resp = await _http.PostAsJsonAsync("translate", payload, ct);
            HttpResponseMessage? responseToUse = resp;

            if (!resp.IsSuccessStatusCode)
            {
                resp.Dispose();
                var respAlt = await _http.PostAsJsonAsync("api/translate", payload, ct);
                responseToUse = respAlt;
            }

            if (responseToUse!.IsSuccessStatusCode)
            {
                var doc = await responseToUse.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
                var translated = TryReadStringArray(doc);
                if (translated != null && translated.Length == texts.Length)
                {
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        result[ordered[i].Key] = translated[i] ?? string.Empty;
                    }
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EasyNMT batch translation not available; falling back to per-item requests");
        }

        // Fallback: call TranslateAsync for each
        foreach (var (k, v) in items)
        {
            var t = await TranslateAsync(v, targetLanguage, sourceLanguage, ct);
            result[k] = t;
        }
        return result;
    }

    private async Task<string> ReadSingleTranslationAsync(HttpResponseMessage response, string fallback, CancellationToken ct)
    {
        try
        {
            response.EnsureSuccessStatusCode();
            var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);

            // Try { "translated": "..." } (EasyNMT 2.x format)
            if (doc?.RootElement.TryGetProperty("translated", out var t2) == true)
            {
                var translated = t2.GetString();
                if (!string.IsNullOrWhiteSpace(translated))
                {
                    _logger.LogInformation("EasyNMT translation successful: {Translation}", translated.Substring(0, Math.Min(50, translated.Length)));
                    return translated;
                }
            }
            // Try { "translation": "..." } (older format)
            if (doc?.RootElement.TryGetProperty("translation", out var t1) == true)
            {
                var translation = t1.GetString();
                if (!string.IsNullOrWhiteSpace(translation))
                {
                    _logger.LogInformation("EasyNMT translation successful: {Translation}", translation.Substring(0, Math.Min(50, translation.Length)));
                    return translation;
                }
            }
            // Try array { "translations": [ "..." ] }
            if (doc?.RootElement.TryGetProperty("translations", out var arr) == true && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
            {
                var translation = arr[0].GetString();
                if (!string.IsNullOrWhiteSpace(translation))
                {
                    _logger.LogInformation("EasyNMT translation successful: {Translation}", translation.Substring(0, Math.Min(50, translation.Length)));
                    return translation;
                }
            }

            _logger.LogWarning("EasyNMT response did not contain expected translation field");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse EasyNMT response");
        }
        return fallback;
    }

    private static string[]? TryReadStringArray(JsonDocument? doc)
    {
        if (doc == null) return null;
        if (doc.RootElement.TryGetProperty("translations", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string?>();
            foreach (var el in arr.EnumerateArray())
            {
                list.Add(el.GetString());
            }
            return list.Select(x => x ?? string.Empty).ToArray();
        }
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string?>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                list.Add(el.GetString());
            }
            return list.Select(x => x ?? string.Empty).ToArray();
        }
        return null;
    }
}