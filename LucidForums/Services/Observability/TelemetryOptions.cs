using LucidForums.Helpers;

namespace LucidForums.Services.Observability;

public class TelemetryOptions : IConfigSection
{
    public static string Section => "Telemetry";

    // Activity/Meter identity (also duplicated in TelemetryConstants for OTLP setup)
    public string ActivitySourceName { get; set; } = TelemetryConstants.ActivitySourceName;
    public string MeterName { get; set; } = TelemetryConstants.MeterName;

    // Metrics names
    public MetricsSection Metrics { get; set; } = new();

    // Tag keys used across spans
    public TagsSection Tags { get; set; } = new();

    // Activity names used across services/providers
    public ActivitiesSection Activities { get; set; } = new();

    // Provider-specific API paths/defaults
    public ApiPathsSection ApiPaths { get; set; } = new();

    public class MetricsSection
    {
        public string TextRequestsCounter { get; set; } = "ai.text.requests";
        public string TextRequestsLatencyHistogram { get; set; } = "ai.text.requests.duration.ms";
    }

    public class TagsSection
    {
        public string Provider { get; set; } = "ai.provider";
        public string System { get; set; } = "ai.system";
        public string Model { get; set; } = "ai.model";
        public string InputLength { get; set; } = "ai.input.length";
        public string OutputLength { get; set; } = "ai.output.length";
        public string TargetLanguage { get; set; } = "ai.target_language";
        public string Streaming { get; set; } = "ai.streaming";
        public string Error { get; set; } = "error";
        public string ExceptionType { get; set; } = "exception.type";
        public string ExceptionMessage { get; set; } = "exception.message";
        public string DurationMs { get; set; } = "duration.ms";
    }

    public class ActivitiesSection
    {
        // TextAiService
        public string TextGenerate { get; set; } = "TextAiService.Generate";
        public string TextTranslate { get; set; } = "TextAiService.Translate";
        public string TextTranslateStream { get; set; } = "TextAiService.TranslateStream";

        // Ollama provider
        public string OllamaGenerate { get; set; } = "Ollama.Generate";
        public string OllamaTranslate { get; set; } = "Ollama.Translate";
        public string OllamaTranslateStream { get; set; } = "Ollama.TranslateStream";

        // LmStudio provider
        public string LmStudioGenerate { get; set; } = "LmStudio.Generate";
        public string LmStudioTranslate { get; set; } = "LmStudio.Translate";
        public string LmStudioTranslateStream { get; set; } = "LmStudio.TranslateStream";
    }

    public class ApiPathsSection
    {
        public string OllamaGeneratePath { get; set; } = "/api/generate";
        public string LmStudioChatCompletionsPath { get; set; } = "/v1/chat/completions";
        public string LmStudioDefaultBaseUrl { get; set; } = "http://localhost:1234";
    }
}
