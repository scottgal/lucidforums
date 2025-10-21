using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using LucidForums.Models.Entities;

namespace LucidForums.Services.Ai.Providers;

public class LmStudioChatProvider : IChatProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<AiOptions> _aiOptions;
    private readonly Services.Observability.ITelemetry _telemetry;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<Services.Observability.TelemetryOptions> _telemetryOptions;

    public LmStudioChatProvider(IHttpClientFactory httpClientFactory, Microsoft.Extensions.Options.IOptionsMonitor<AiOptions> aiOptions, Services.Observability.ITelemetry telemetry, Microsoft.Extensions.Options.IOptionsMonitor<Services.Observability.TelemetryOptions> telemetryOptions)
    {
        _httpClientFactory = httpClientFactory;
        _aiOptions = aiOptions;
        _telemetry = telemetry;
        _telemetryOptions = telemetryOptions;
    }

    public string Name => "lmstudio";

    private Uri GetBaseUri()
    {
        var cur = _aiOptions.CurrentValue;
        var defaultUrl = _telemetryOptions.CurrentValue.ApiPaths.LmStudioDefaultBaseUrl;
        var baseUrl = string.IsNullOrWhiteSpace(cur.Endpoint) ? defaultUrl : cur.Endpoint!;
        if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) baseUrl = "http://" + baseUrl;
        return new Uri(baseUrl);
    }

    public async Task<string> GenerateAsync(Charter charter, string userInput, string? model, double? temperature, int? maxTokens, CancellationToken ct)
    {
        var curOpts = _aiOptions.CurrentValue;
        var activeModel = string.IsNullOrWhiteSpace(model) ? (curOpts.DefaultModel ?? "lmstudio-default") : model!;
        var tcfg = _telemetryOptions.CurrentValue;
        using var activity = _telemetry.StartActivity(tcfg.Activities.LmStudioGenerate, ActivityKind.Client, a =>
        {
            a?.SetTag(tcfg.Tags.System, "lmstudio");
            a?.SetTag(tcfg.Tags.Model, activeModel);
            a?.SetTag(tcfg.Tags.InputLength, userInput?.Length ?? 0);
        });

        var messages = new object[]
        {
            new { role = "system", content = charter.BuildSystemPrompt() },
            new { role = "user", content = userInput }
        };

        var payload = new
        {
            model = activeModel,
            messages,
            temperature = temperature ?? curOpts.Temperature,
            max_tokens = maxTokens ?? curOpts.MaxTokens,
            stream = false
        };

        try
        {
            var client = _httpClientFactory.CreateClient("ollama"); // we'll send absolute URI
            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(GetBaseUri(), _telemetryOptions.CurrentValue.ApiPaths.LmStudioChatCompletionsPath))
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            using var resp = await client.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            // OpenAI-compatible: choices[0].message.content
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var msgObj) && msgObj.TryGetProperty("content", out var content))
                {
                    var result = content.GetString() ?? string.Empty;
                    activity?.SetTag(tcfg.Tags.OutputLength, result.Length);
                    return result;
                }
            }
            return string.Empty;
        }
        catch (Exception ex)
        {
            var tags = _telemetryOptions.CurrentValue.Tags;
            activity?.SetTag(tags.Error, true);
            activity?.SetTag(tags.ExceptionType, ex.GetType().FullName);
            activity?.SetTag(tags.ExceptionMessage, ex.Message);
            throw;
        }
    }

    public Task<string> TranslateAsync(string text, string targetLanguage, string? model, double? temperature, int? maxTokens, CancellationToken ct)
    {
        var charter = new Charter
        {
            Name = "Translator",
            Purpose = "Translate user-provided text into the specified target language while preserving formatting, markdown and links. Only output the translated text without any preface.",
        };
        var user = $"Target language: {targetLanguage}\n\nText to translate:\n\n{text}";
        return GenerateAsync(charter, user, model, temperature, maxTokens, ct);
    }

    public async Task TranslateStreamAsync(string text, string targetLanguage, string? model, double? temperature, int? maxTokens, Func<string, Task> onChunk, CancellationToken ct)
    {
        // Try to use streaming chat completions if supported
        var curOpts = _aiOptions.CurrentValue;
        var activeModel = string.IsNullOrWhiteSpace(model) ? (curOpts.DefaultModel ?? "lmstudio-default") : model!;
        var tcfg = _telemetryOptions.CurrentValue;
        using var activity = _telemetry.StartActivity(tcfg.Activities.LmStudioTranslateStream, ActivityKind.Client, a =>
        {
            a?.SetTag(tcfg.Tags.System, "lmstudio");
            a?.SetTag(tcfg.Tags.Model, activeModel);
            a?.SetTag(tcfg.Tags.TargetLanguage, targetLanguage);
            a?.SetTag(tcfg.Tags.InputLength, text?.Length ?? 0);
            a?.SetTag(tcfg.Tags.Streaming, true);
        });

        var messages = new object[]
        {
            new { role = "system", content = "You are a professional translator. Translate the user's text into the target language while preserving the original formatting, markdown and links. Only output the translated text without any preface." },
            new { role = "user", content = $"Target language: {targetLanguage}\n\n{text}" }
        };
        var payload = new
        {
            model = activeModel,
            messages,
            temperature = temperature ?? curOpts.Temperature,
            max_tokens = maxTokens ?? curOpts.MaxTokens,
            stream = true
        };

        try
        {
            var client = _httpClientFactory.CreateClient("ollama");
            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(GetBaseUri(), _telemetryOptions.CurrentValue.ApiPaths.LmStudioChatCompletionsPath))
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[8192];
            var sb = new StringBuilder();
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                while (true)
                {
                    var s = sb.ToString();
                    var idx = s.IndexOf('\n');
                    if (idx < 0) break;
                    var line = s.Substring(0, idx).Trim();
                    sb.Remove(0, idx + 1);
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        var jsonLine = line.Substring(5).Trim();
                        if (jsonLine == "[DONE]") return;
                        try
                        {
                            using var jdoc = JsonDocument.Parse(jsonLine);
                            var root = jdoc.RootElement;
                            if (root.TryGetProperty("choices", out var choices))
                            {
                                var first = choices[0];
                                if (first.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content))
                                {
                                    var piece = content.GetString();
                                    if (!string.IsNullOrEmpty(piece))
                                    {
                                        await onChunk(piece);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            var tags = _telemetryOptions.CurrentValue.Tags;
            activity?.SetTag(tags.Error, true);
            activity?.SetTag(tags.ExceptionType, ex.GetType().FullName);
            activity?.SetTag(tags.ExceptionMessage, ex.Message);
            throw;
        }
    }
}
