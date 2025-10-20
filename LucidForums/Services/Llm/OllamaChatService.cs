using System.Text;
using LucidForums.Models.Entities;
using OllamaSharp;
using OllamaSharp.Models;

namespace LucidForums.Services.Llm;

public class OllamaChatService(IHttpClientFactory httpClientFactory, IOllamaEndpointProvider endpointProvider) : IOllamaChatService
{
    public async Task<string> ChatAsync(Charter charter, string userInput, string? model = null, CancellationToken ct = default)
    {
        var options = endpointProvider.Options;
        var activeModel = string.IsNullOrWhiteSpace(model) ? options.DefaultModel : model;

        var prompt = new StringBuilder()
            .AppendLine(charter.BuildSystemPrompt())
            .AppendLine()
            .AppendLine("User:")
            .AppendLine(userInput)
            .AppendLine()
            .Append("Assistant:")
            .ToString();

        var client = httpClientFactory.CreateClient("ollama");
        var request = new
        {
            model = activeModel,
            prompt,
            options = new { temperature = options.Temperature, num_predict = options.MaxTokens }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(endpointProvider.GetBaseAddress(), "/api/generate"))
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);

        // The /api/generate simple (non-streaming) response contains a `response` field
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("response", out var respEl) ? respEl.GetString() ?? string.Empty : string.Empty;
    }
}