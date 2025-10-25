using System.Net.Http.Json;
using System.Text.Json;
using mostlylucid.llmtranslate.Services;
using Microsoft.Extensions.Logging;

namespace mostlylucid.llmtranslate.Services.Providers;

public class OllamaTranslationProvider : IAiTranslationProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaTranslationProvider> _logger;
    private readonly string _model;

    public OllamaTranslationProvider(HttpClient httpClient, ILogger<OllamaTranslationProvider> logger, string model = "llama3.2", string? baseUrl = null)
    {
        _http = httpClient;
        _logger = logger;
        _model = model;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            _http.BaseAddress = new Uri(baseUrl);
        }
        if (_http.BaseAddress == null)
        {
            _http.BaseAddress = new Uri("http://localhost:11434/");
        }
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = "en", CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                model = _model,
                prompt = $"Translate from {sourceLanguage ?? "auto"} to {targetLanguage}: {text}",
                stream = false
            };
            using var resp = await _http.PostAsJsonAsync("api/generate", body, ct);
            resp.EnsureSuccessStatusCode();
            var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            var content = doc?.RootElement.TryGetProperty("response", out var r) == true ? r.GetString() : null;
            return string.IsNullOrWhiteSpace(content) ? text : content!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ollama translation failed");
            return text;
        }
    }

    public async Task<Dictionary<string, string>> TranslateBatchAsync(Dictionary<string, string> items, string targetLanguage, string? sourceLanguage = "en", CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(items.Select(kvp => new { key = kvp.Key, text = kvp.Value }));
        var prompt = $"Translate the following UI strings from {sourceLanguage ?? "auto"} to {targetLanguage}.\n" +
                     "Return ONLY a JSON array of objects with properties \"key\" and \"translated\".\n" +
                     "Preserve placeholders like {{0}} and {{name}}, and any HTML tags/entities.\n\n" +
                     "Input:\n" + payload;

        var answer = await TranslateAsync(prompt, targetLanguage, sourceLanguage, ct);
        var parsed = TryParseBatch(answer);
        if (parsed.Count == 0)
        {
            foreach (var (k, v) in items)
            {
                parsed[k] = await TranslateAsync(v, targetLanguage, sourceLanguage, ct);
            }
        }
        return parsed;
    }

    private static Dictionary<string, string> TryParseBatch(string? text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return result;
        try
        {
            var arrayText = ExtractFirstJsonArray(text) ?? text;
            var items = JsonSerializer.Deserialize<List<BatchItem>>(arrayText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (items != null)
            {
                foreach (var it in items)
                {
                    if (!string.IsNullOrWhiteSpace(it.Key))
                        result[it.Key] = it.Translated ?? string.Empty;
                }
            }
        }
        catch
        { 
        }
        return result;
    }

    private static string? ExtractFirstJsonArray(string input)
    {
        int start = -1, depth = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '[')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (input[i] == ']')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    return input.Substring(start, i - start + 1);
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
