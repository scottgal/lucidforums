using LucidForums.Helpers;

namespace LucidForums.Services.Ai;

public class AiOptions : IConfigSection
{
    public static string Section => "AI";

    public string? Provider { get; set; } // "OpenAI" | "Ollama" | "AzureOpenAI" | etc.
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; } // When using self-hosted or Azure
    public string? DefaultModel { get; set; }
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 512;
}