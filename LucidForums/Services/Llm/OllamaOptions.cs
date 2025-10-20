namespace LucidForums.Services.Llm;

public class OllamaOptions
{
    // Base endpoint, e.g., http://ollama:11434 (in Docker) or http://localhost:11434 (local)
    public string Endpoint { get; set; } = "http://localhost:11434";

    // Default model to use if callers don't specify one
    public string DefaultModel { get; set; } = "llama3.1";

    // Optional generation parameters
    public double? Temperature { get; set; } = 0.2;
    public int? MaxTokens { get; set; } = 512;
    public TimeSpan? Timeout { get; set; } = TimeSpan.FromSeconds(60);
}