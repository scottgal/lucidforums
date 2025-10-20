using System;
using System.Buffers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LucidForums.Models.Entities;
using LucidForums.Services.Llm;

namespace LucidForums.Services.Ai;

public class TextAiService : ITextAiService
{
    private readonly IHttpClientFactory _httpClientFactory; // Current implementation uses Ollama HTTP
    private readonly IOllamaEndpointProvider _ollamaEndpointProvider;

    public TextAiService(
        IHttpClientFactory httpClientFactory,
        IOllamaEndpointProvider ollamaEndpointProvider)
    {
        _httpClientFactory = httpClientFactory;
        _ollamaEndpointProvider = ollamaEndpointProvider;
    }

    public async Task<string> GenerateAsync(Charter charter, string userInput, string? model = null, CancellationToken ct = default)
    {
        var optionsFallback = _ollamaEndpointProvider.Options;
        var activeModel = string.IsNullOrWhiteSpace(model) ? optionsFallback.DefaultModel : model;

        var prompt = new StringBuilder()
            .AppendLine(charter.BuildSystemPrompt())
            .AppendLine()
            .AppendLine("User:")
            .AppendLine(userInput)
            .AppendLine()
            .Append("Assistant:")
            .ToString();

        var client = _httpClientFactory.CreateClient("ollama");
        var request = new
        {
            model = activeModel,
            prompt,
            options = new { temperature = optionsFallback.Temperature, num_predict = optionsFallback.MaxTokens }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(_ollamaEndpointProvider.GetBaseAddress(), "/api/generate"))
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);

        using var doc = System.Text.Json.JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("response", out var respEl) ? respEl.GetString() ?? string.Empty : string.Empty;
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        var charter = new Charter
        {
            Name = "Translator",
            Purpose = "Translate user-provided text into the specified target language while preserving formatting, markdown and links. Only output the translated text without any preface.",
        };
        var user = $"Target language: {targetLanguage}\n\nText to translate:\n\n{text}";
        return await GenerateAsync(charter, user, null, ct);
    }

    public async Task TranslateStreamAsync(string text, string targetLanguage, Func<string, Task> onChunk, CancellationToken ct = default)
    {
        // Attempt to stream from Ollama if possible, otherwise fall back to chunking the final translation.
        try
        {
            var optionsFallback = _ollamaEndpointProvider.Options;
            var activeModel = optionsFallback.DefaultModel;
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

            var client = _httpClientFactory.CreateClient("ollama");
            var request = new
            {
                model = activeModel,
                prompt,
                stream = true,
                options = new { temperature = optionsFallback.Temperature, num_predict = optionsFallback.MaxTokens }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(_ollamaEndpointProvider.GetBaseAddress(), "/api/generate"))
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
                    var idx = sb.ToString().IndexOf('\n');
                    if (idx < 0) break;
                    var line = sb.ToString().Substring(0, idx).Trim();
                    sb.Remove(0, idx + 1);
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    // Ollama streams NDJSON lines
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
                    catch
                    {
                        // ignore malformed line
                    }
                }
            }
            return;
        }
        catch
        {
            // Fallback: non-streaming translate then chunk by words
            var full = await TranslateAsync(text, targetLanguage, ct);
            var words = full.Split(' ');
            foreach (var w in words)
            {
                await onChunk((w.Length > 0 ? w : "") + " ");
            }
        }
    }
}