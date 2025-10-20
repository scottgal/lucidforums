using System.Text.Json;
using LucidForums.Models.Entities;
using LucidForums.Services.Llm;
using System.Text;

namespace LucidForums.Services.Moderation;

public class ModerationService(IHttpClientFactory httpClientFactory, IOllamaEndpointProvider endpointProvider) : IModerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ModerationResult> EvaluatePostAsync(Charter charter, string content, string? model = null, CancellationToken ct = default)
    {
        var options = endpointProvider.Options;
        var activeModel = string.IsNullOrWhiteSpace(model) ? options.DefaultModel : model;

        // Prompt template instructing strict JSON output
        var userPrompt = $$"""
        Evaluate the following user post against the community charter. Decide whether to ALLOW, FLAG (needs moderator review), or REJECT (clear violation).
        Return a strict JSON object with keys: decision ("allow"|"flag"|"reject"), summary (short text), violations (array of strings referencing rules).

        Post:
        ```
        {content}
        ```
        """;

        var prompt = new StringBuilder()
            .AppendLine(charter.BuildSystemPrompt())
            .AppendLine()
            .AppendLine(userPrompt)
            .ToString();

        var client = httpClientFactory.CreateClient("ollama");
        var request = new
        {
            model = activeModel,
            prompt,
            options = new { temperature = 0.0, num_predict = options.MaxTokens }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(endpointProvider.GetBaseAddress(), "/api/generate"))
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);

        var text = ExtractResponseText(body);

        // Try to parse the JSON response
        try
        {
            var parsed = JsonSerializer.Deserialize<RawModerationResponse>(text, JsonOptions);
            if (parsed is null)
            {
                return Fallback("Empty response from model", new());
            }

            var decision = parsed.Decision?.ToLowerInvariant() switch
            {
                "allow" => ModerationDecision.Allow,
                "flag" => ModerationDecision.Flag,
                "reject" => ModerationDecision.Reject,
                _ => ModerationDecision.Flag
            };

            return new ModerationResult
            {
                Decision = decision,
                Summary = parsed.Summary ?? string.Empty,
                Violations = parsed.Violations ?? new List<string>()
            };
        }
        catch
        {
            // If parsing fails, flag for safety
            return Fallback("Unable to parse model output", new List<string> { "ParsingError" });
        }
    }

    private static string ExtractResponseText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("response", out var el))
            {
                return el.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // ignore
        }
        return body;
    }

    private static ModerationResult Fallback(string summary, List<string> violations) => new()
    {
        Decision = ModerationDecision.Flag,
        Summary = summary,
        Violations = violations
    };

    private class RawModerationResponse
    {
        public string? Decision { get; set; }
        public string? Summary { get; set; }
        public List<string>? Violations { get; set; }
    }
}