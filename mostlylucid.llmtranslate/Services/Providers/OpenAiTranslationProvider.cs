using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using mostlylucid.llmtranslate.Services;
using Microsoft.Extensions.Logging;

namespace mostlylucid.llmtranslate.Services.Providers;

public class OpenAiTranslationProvider : IAiTranslationProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenAiTranslationProvider> _logger;
    private readonly string _model;

    public OpenAiTranslationProvider(HttpClient httpClient, ILogger<OpenAiTranslationProvider> logger, string apiKey, string model = "gpt-4o-mini")
    {
        _http = httpClient;
        _logger = logger;
        _model = model;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        if (_http.BaseAddress == null)
        {
            _http.BaseAddress = new Uri("https://api.openai.com/");
        }
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = "en", CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = $"Translate from {sourceLanguage ?? "auto"} to {targetLanguage}. Preserve placeholders like {{0}} or {{name}}, and HTML tags." },
                    new { role = "user", content = text }
                }
            };

            using var resp = await _http.PostAsJsonAsync("v1/chat/completions", body, ct);
            resp.EnsureSuccessStatusCode();
            var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            var content = doc?.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return string.IsNullOrWhiteSpace(content) ? text : content!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI translation failed");
            return text;
        }
    }

    public async Task<Dictionary<string, string>> TranslateBatchAsync(Dictionary<string, string> items, string targetLanguage, string? sourceLanguage = "en", CancellationToken ct = default)
    {
        // Build prompt requesting strict JSON array
        var payload = JsonSerializer.Serialize(items.Select(kvp => new { key = kvp.Key, text = kvp.Value }));
        var prompt = $"Translate the following UI strings from {sourceLanguage ?? "auto"} to {targetLanguage}.\n" +
                     "Return ONLY a JSON array of objects with properties \"key\" and \"translated\".\n" +
                     "Preserve placeholders like {{0}} and {{name}}, and any HTML tags/entities.\n\n" +
                     "Input:\n" + payload;

        var answer = await TranslateAsync(prompt, targetLanguage, sourceLanguage, ct);
        var parsed = TryParseBatch(answer);
        if (parsed.Count == 0)
        {
            // Fallback to individual
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
            // ignore
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
