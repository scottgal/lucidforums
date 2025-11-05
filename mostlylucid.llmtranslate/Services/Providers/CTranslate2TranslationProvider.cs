using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace mostlylucid.llmtranslate.Services.Providers;

/// <summary>
/// IAiTranslationProvider implementation that talks to an OpenNMT CTranslate2 REST server.
/// Expected endpoints (vary by image/build):
///  - POST /translate with JSON body containing text(s) and language codes.
///    Common payload shapes we try in order:
///      { "text": string or string[], "source": "en", "target": "de" }
///      { "text": string or string[], "src_lang": "en", "tgt_lang": "de" }
///  - Responses may look like:
///      { "translation": string }
///      { "translations": string[] }
///      { "output": string[] }
/// The provider is defensive and will attempt multiple shapes before failing soft by returning input text.
/// </summary>
public class CTranslate2TranslationProvider : IAiTranslationProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<CTranslate2TranslationProvider> _logger;

    /// <param name="baseUrl">Base URL of the ctranslate2 server, e.g. http://localhost:5000/</param>
    public CTranslate2TranslationProvider(HttpClient httpClient, ILogger<CTranslate2TranslationProvider> logger, string? baseUrl = null)
    {
        _http = httpClient;
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            _http.BaseAddress = new Uri(baseUrl);
        }
        if (_http.BaseAddress == null)
        {
            _http.BaseAddress = new Uri("http://localhost:5000/");
        }
        if (_http.Timeout < TimeSpan.FromSeconds(120))
        {
            _http.Timeout = TimeSpan.FromSeconds(120);
        }
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = "en", CancellationToken ct = default)
    {
        try
        {
            // Primary payload shape
            var payload1 = new { text = (object)text, source = sourceLanguage, target = targetLanguage };
            using var resp1 = await _http.PostAsJsonAsync("translate", payload1, ct);
            if (resp1.IsSuccessStatusCode)
            {
                return await ReadSingleTranslationAsync(resp1, text, ct);
            }
            var err1 = await SafeReadContentAsync(resp1, ct);
            _logger.LogWarning("CTranslate2 /translate payload1 failed: {Code} {Err}", resp1.StatusCode, err1);

            // Alternative payload shape used by some servers
            var payload2 = new { text = (object)text, src_lang = sourceLanguage, tgt_lang = targetLanguage };
            using var resp2 = await _http.PostAsJsonAsync("translate", payload2, ct);
            if (resp2.IsSuccessStatusCode)
            {
                return await ReadSingleTranslationAsync(resp2, text, ct);
            }
            var err2 = await SafeReadContentAsync(resp2, ct);
            _logger.LogWarning("CTranslate2 /translate payload2 failed: {Code} {Err}", resp2.StatusCode, err2);

            // Some deployments use /api/translate
            using var resp3 = await _http.PostAsJsonAsync("api/translate", payload1, ct);
            if (resp3.IsSuccessStatusCode)
            {
                return await ReadSingleTranslationAsync(resp3, text, ct);
            }
            var err3 = await SafeReadContentAsync(resp3, ct);
            _logger.LogWarning("CTranslate2 /api/translate payload1 failed: {Code} {Err}", resp3.StatusCode, err3);

            using var resp4 = await _http.PostAsJsonAsync("api/translate", payload2, ct);
            if (resp4.IsSuccessStatusCode)
            {
                return await ReadSingleTranslationAsync(resp4, text, ct);
            }
            var err4 = await SafeReadContentAsync(resp4, ct);
            _logger.LogError("CTranslate2 /api/translate payload2 failed: {Code} {Err}", resp4.StatusCode, err4);

            // fail-soft
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CTranslate2 translation failed for target {Target}", targetLanguage);
            return text; // fail soft
        }
    }

    public async Task<Dictionary<string, string>> TranslateBatchAsync(Dictionary<string, string> items, string targetLanguage, string? sourceLanguage = "en", CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (items.Count == 0) return result;
        try
        {
            var ordered = items.ToList();
            var texts = ordered.Select(kvp => kvp.Value).ToArray();

            // primary batch payload
            var payload1 = new { text = (object)texts, source = sourceLanguage, target = targetLanguage };
            using var resp1 = await _http.PostAsJsonAsync("translate", payload1, ct);
            if (resp1.IsSuccessStatusCode)
            {
                var arr = await ReadStringArrayAsync(resp1, ct);
                if (arr != null && arr.Length == texts.Length)
                {
                    for (int i = 0; i < ordered.Count; i++)
                        result[ordered[i].Key] = arr[i];
                    return result;
                }
            }

            // alternative batch payload
            var payload2 = new { text = (object)texts, src_lang = sourceLanguage, tgt_lang = targetLanguage };
            using var resp2 = await _http.PostAsJsonAsync("translate", payload2, ct);
            if (resp2.IsSuccessStatusCode)
            {
                var arr = await ReadStringArrayAsync(resp2, ct);
                if (arr != null && arr.Length == texts.Length)
                {
                    for (int i = 0; i < ordered.Count; i++)
                        result[ordered[i].Key] = arr[i];
                    return result;
                }
            }

            // fallback to per-item to be robust
            foreach (var (key, value) in ordered)
            {
                result[key] = await TranslateAsync(value, targetLanguage, sourceLanguage, ct);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CTranslate2 batch translation failed for target {Target}", targetLanguage);
            // fail-soft: return originals
            foreach (var kvp in items)
                result[kvp.Key] = kvp.Value;
            return result;
        }
    }

    private async Task<string> ReadSingleTranslationAsync(HttpResponseMessage response, string fallback, CancellationToken ct)
    {
        try
        {
            using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            if (doc == null) return fallback;

            // Try various response shapes
            if (doc.RootElement.TryGetProperty("translation", out var single) && single.ValueKind == JsonValueKind.String)
                return single.GetString() ?? fallback;

            if (doc.RootElement.TryGetProperty("translations", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                return arr[0].GetString() ?? fallback;

            if (doc.RootElement.TryGetProperty("output", out var outArr) && outArr.ValueKind == JsonValueKind.Array && outArr.GetArrayLength() > 0)
                return outArr[0].GetString() ?? fallback;

            // Some servers return { "text": ["..."] }
            if (doc.RootElement.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.Array && textProp.GetArrayLength() > 0)
                return textProp[0].GetString() ?? fallback;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse CTranslate2 single translation response");
        }
        return fallback;
    }

    private async Task<string[]?> ReadStringArrayAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            if (doc == null) return null;

            if (doc.RootElement.TryGetProperty("translations", out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();

            if (doc.RootElement.TryGetProperty("output", out var outArr) && outArr.ValueKind == JsonValueKind.Array)
                return outArr.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();

            if (doc.RootElement.TryGetProperty("text", out var textArr) && textArr.ValueKind == JsonValueKind.Array)
                return textArr.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse CTranslate2 batch translation response");
        }
        return null;
    }

    private static async Task<string> SafeReadContentAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch { return string.Empty; }
    }
}
