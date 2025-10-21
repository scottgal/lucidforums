using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using LucidForums.Models.Entities;
using LucidForums.Services.Llm;

namespace LucidForums.Services.Ai.Providers;

public class OllamaChatProvider : IChatProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOllamaEndpointProvider _ollama;
    private readonly Services.Observability.ITelemetry _telemetry;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<Services.Observability.TelemetryOptions> _telemetryOptions;

    public OllamaChatProvider(IHttpClientFactory httpClientFactory, IOllamaEndpointProvider ollama, Services.Observability.ITelemetry telemetry, Microsoft.Extensions.Options.IOptionsMonitor<Services.Observability.TelemetryOptions> telemetryOptions)
    {
        _httpClientFactory = httpClientFactory;
        _ollama = ollama;
        _telemetry = telemetry;
        _telemetryOptions = telemetryOptions;
    }

    public string Name => "ollama";

    public async Task<string> GenerateAsync(Charter charter, string userInput, string? model, double? temperature, int? maxTokens, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var optionsFallback = _ollama.Options;
        var activeModel = string.IsNullOrWhiteSpace(model) ? optionsFallback.DefaultModel : model!;
        var prompt = new StringBuilder()
            .AppendLine(charter.BuildSystemPrompt())
            .AppendLine()
            .AppendLine("User:")
            .AppendLine(userInput)
            .AppendLine()
            .Append("Assistant:")
            .ToString();

        var tcfg = _telemetryOptions.CurrentValue;
        using var activity = _telemetry.StartActivity(tcfg.Activities.OllamaGenerate, ActivityKind.Client, a =>
        {
            a?.SetTag(tcfg.Tags.System, "ollama");
            a?.SetTag(tcfg.Tags.Model, activeModel);
            a?.SetTag(tcfg.Tags.InputLength, userInput?.Length ?? 0);
        });

        try
        {
            var client = _httpClientFactory.CreateClient("ollama");
            var request = new
            {
                model = activeModel,
                prompt,
                options = new { temperature = temperature ?? optionsFallback.Temperature, num_predict = maxTokens ?? optionsFallback.MaxTokens }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(_ollama.GetBaseAddress(), _telemetryOptions.CurrentValue.ApiPaths.OllamaGeneratePath))
            {
                Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
            };

            using var response = await client.SendAsync(httpRequest, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var result = doc.RootElement.TryGetProperty("response", out var respEl) ? respEl.GetString() ?? string.Empty : string.Empty;
            activity?.SetTag(tcfg.Tags.OutputLength, result?.Length ?? 0);
            return result;
        }
        catch (Exception ex)
        {
            var tags = _telemetryOptions.CurrentValue.Tags;
            activity?.SetTag(tags.Error, true);
            activity?.SetTag(tags.ExceptionType, ex.GetType().FullName);
            activity?.SetTag(tags.ExceptionMessage, ex.Message);
            throw;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag(_telemetryOptions.CurrentValue.Tags.DurationMs, sw.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, string? model, double? temperature, int? maxTokens, CancellationToken ct)
    {
        var tcfg = _telemetryOptions.CurrentValue;
        using var activity = _telemetry.StartActivity(tcfg.Activities.OllamaTranslate, ActivityKind.Client, a =>
        {
            a?.SetTag(tcfg.Tags.System, "ollama");
            a?.SetTag(tcfg.Tags.TargetLanguage, targetLanguage);
            a?.SetTag(tcfg.Tags.InputLength, text?.Length ?? 0);
        });
        try
        {
            var charter = new Charter
            {
                Name = "Translator",
                Purpose = "Translate user-provided text into the specified target language while preserving formatting, markdown and links. Only output the translated text without any preface.",
            };
            var user = $"Target language: {targetLanguage}\n\nText to translate:\n\n{text}";
            var result = await GenerateAsync(charter, user, model, temperature, maxTokens, ct);
            activity?.SetTag(tcfg.Tags.OutputLength, result?.Length ?? 0);
            return result;
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

    public async Task TranslateStreamAsync(string text, string targetLanguage, string? model, double? temperature, int? maxTokens, Func<string, Task> onChunk, CancellationToken ct)
    {
        var optionsFallback = _ollama.Options;
        var activeModel = string.IsNullOrWhiteSpace(model) ? optionsFallback.DefaultModel : model!;
        var system = "You are a professional translator. Translate the user's text into the target language while preserving the original formatting, markdown and links. Only output the translated text without any preface.";
        var prompt = new StringBuilder()
            .AppendLine(system)
            .AppendLine()
            .AppendLine("User:")
            .AppendLine($"Target language: {targetLanguage}")
            .AppendLine()
            .AppendLine("Text to translate:")
            .AppendLine()
            .AppendLine(text)
            .AppendLine()
            .Append("Assistant:")
            .ToString();

        var tcfg = _telemetryOptions.CurrentValue;
        using var activity = _telemetry.StartActivity(tcfg.Activities.OllamaTranslateStream, ActivityKind.Client, a =>
        {
            a?.SetTag(tcfg.Tags.System, "ollama");
            a?.SetTag(tcfg.Tags.Model, activeModel);
            a?.SetTag(tcfg.Tags.TargetLanguage, targetLanguage);
            a?.SetTag(tcfg.Tags.InputLength, text?.Length ?? 0);
            a?.SetTag(tcfg.Tags.Streaming, true);
        });

        try
        {
            var client = _httpClientFactory.CreateClient("ollama");
            var request = new
            {
                model = activeModel,
                prompt,
                stream = true,
                options = new { temperature = temperature ?? optionsFallback.Temperature, num_predict = maxTokens ?? optionsFallback.MaxTokens }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(_ollama.GetBaseAddress(), _telemetryOptions.CurrentValue.ApiPaths.OllamaGeneratePath))
            {
                Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
            };

            using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
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
                    try
                    {
                        using var json = JsonDocument.Parse(line);
                        if (json.RootElement.TryGetProperty("response", out var chunkEl))
                        {
                            var piece = chunkEl.GetString();
                            if (!string.IsNullOrEmpty(piece))
                            {
                                await onChunk(piece);
                            }
                        }
                    }
                    catch { }
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
