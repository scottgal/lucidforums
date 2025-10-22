using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using LucidForums.Models.Entities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.AI;

namespace LucidForums.Services.Ai;

public class TextAiService : ITextAiService
{
    private readonly IEnumerable<IChatProvider> _providers;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly Services.Observability.ITelemetry _telemetry;
    private readonly Histogram<double> _latency;
    private readonly Counter<long> _requests;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<Services.Observability.TelemetryOptions> _telemetryOptions;
    private readonly IAiSettingsService _aiSettings;

    public TextAiService(IEnumerable<IChatProvider> providers, IOptionsMonitor<AiOptions> options, Services.Observability.ITelemetry telemetry, Microsoft.Extensions.Options.IOptionsMonitor<Services.Observability.TelemetryOptions> telemetryOptions, IAiSettingsService? aiSettings = null)
    {
        _providers = providers;
        _options = options;
        _telemetry = telemetry;
        _telemetryOptions = telemetryOptions;
        _aiSettings = aiSettings ?? new FallbackAiSettings(options);
        var t = _telemetryOptions.CurrentValue;
        _latency = _telemetry.GetHistogram(t.Metrics.TextRequestsLatencyHistogram);
        _requests = _telemetry.GetCounter(t.Metrics.TextRequestsCounter);
    }

    private sealed class FallbackAiSettings : IAiSettingsService
    {
        private readonly IOptionsMonitor<AiOptions> _opts;
        public FallbackAiSettings(IOptionsMonitor<AiOptions> opts) { _opts = opts; }
        public string? GenerationProvider { get; set; }
        public string? GenerationModel { get; set; }
        public string? TranslationProvider { get; set; }
        public string? TranslationModel { get; set; }
        public string? EmbeddingProvider { get; set; }
        public string? EmbeddingModel { get; set; }
        public IReadOnlyList<string> KnownProviders { get; private set; } = Array.Empty<string>();
        public void SetKnownProviders(IEnumerable<string> providers) { KnownProviders = providers.ToList(); }
    }

    private IChatProvider ResolveProvider(string? desired)
    {
        var name = (desired ?? _options.CurrentValue.Provider ?? "ollama").Trim().ToLowerInvariant();
        // prefer exact name match
        var provider = _providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        // fallback: if provider not found, default to first Ollama if available
        provider ??= _providers.FirstOrDefault(p => p.Name.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                    ?? _providers.First();
        return provider;
    }

    public async Task<string> GenerateAsync(Charter charter, string userInput, string? model = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var provider = ResolveProvider(_aiSettings.GenerationProvider);
        var cur = _options.CurrentValue;
        var activeModel = string.IsNullOrWhiteSpace(model) ? (_aiSettings.GenerationModel ?? cur.DefaultModel) : model;
        var tcfg = _telemetryOptions.CurrentValue;
        using var activity = _telemetry.StartActivity(tcfg.Activities.TextGenerate, ActivityKind.Internal, a =>
        {
            a?.SetTag(tcfg.Tags.Provider, provider.Name);
            a?.SetTag(tcfg.Tags.Model, activeModel ?? string.Empty);
            a?.SetTag(tcfg.Tags.InputLength, userInput?.Length ?? 0);
        });
        try
        {
            _requests.Add(1, new KeyValuePair<string, object?>(tcfg.Tags.Provider, provider.Name));
            var result = await provider.GenerateAsync(charter, userInput, activeModel, cur.Temperature, cur.MaxTokens, ct);
            result = Sanitize(result);
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
            _latency.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>(_telemetryOptions.CurrentValue.Tags.Provider, provider.Name));
        }
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        // Back-compat overload: no explicit source; delegate to new overload with sourceLanguage = null (auto-detect)
        return await TranslateAsync(text, targetLanguage, (string?)null, ct);
    }

    private async Task<string> DetectLanguageNameAsync(string text, CancellationToken ct)
    {
        var charter = new Charter
        {
            Name = "LanguageDetector",
            Purpose = "Detect the human language of the provided text and respond with only the English name of the language, e.g., 'English', 'Spanish', 'French'."
        };
        var prompt = new StringBuilder()
            .AppendLine("Return ONLY the language name in English, with no punctuation or extra words.")
            .AppendLine()
            .AppendLine("Text:")
            .AppendLine(text ?? string.Empty)
            .ToString();
        var detected = await GenerateAsync(charter, prompt, _aiSettings.TranslationModel ?? _options.CurrentValue.DefaultModel, ct);
        detected = (detected ?? string.Empty).Trim();
        // Normalize a bit: title case first letter
        if (string.IsNullOrEmpty(detected)) return "English";
        // Some models return codes; map a few common codes
        var low = detected.ToLowerInvariant();
        return low switch
        {
            "en" => "English",
            "es" => "Spanish",
            "fr" => "French",
            "de" => "German",
            "it" => "Italian",
            "pt" => "Portuguese",
            "ru" => "Russian",
            "zh" or "zh-cn" or "zh-hans" => "Chinese",
            "ja" => "Japanese",
            "ko" => "Korean",
            _ => char.ToUpperInvariant(detected[0]) + detected.Substring(1)
        };
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, string? sourceLanguage, CancellationToken ct = default)
    {
        // Detect source language when missing or set to 'auto'
        var src = string.IsNullOrWhiteSpace(sourceLanguage) || sourceLanguage!.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? await DetectLanguageNameAsync(text, ct)
            : sourceLanguage!;
        // If source and target look the same, short-circuit
        if (string.Equals(src, targetLanguage, StringComparison.OrdinalIgnoreCase))
            return text ?? string.Empty;
        // Use provider translate (prompt already instructs to translate to target; most models infer source)
        var sw = Stopwatch.StartNew();
        var provider = ResolveProvider(_aiSettings.TranslationProvider);
        var cur = _options.CurrentValue;
        var tcfg = _telemetryOptions.CurrentValue;
        using var activity = _telemetry.StartActivity(tcfg.Activities.TextTranslate, ActivityKind.Internal, a =>
        {
            a?.SetTag(tcfg.Tags.Provider, provider.Name);
            a?.SetTag(tcfg.Tags.TargetLanguage, targetLanguage);
            a?.SetTag("source.language", src);
            a?.SetTag(tcfg.Tags.InputLength, text?.Length ?? 0);
        });
        try
        {
            _requests.Add(1, new KeyValuePair<string, object?>(tcfg.Tags.Provider, provider.Name));
            // Hint the source language by prefixing a short note; providers don't accept a source param
            var toTranslate = text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(src))
            {
                // Some models do better when told the source explicitly in the input
                toTranslate = $"[Source language: {src}]\n\n" + toTranslate;
            }
            var result = await provider.TranslateAsync(toTranslate, targetLanguage, _aiSettings.TranslationModel ?? cur.DefaultModel, cur.Temperature, cur.MaxTokens, ct);
            result = Sanitize(result);
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
            _latency.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>(_telemetryOptions.CurrentValue.Tags.Provider, provider.Name));
        }
    }

    public async Task TranslateStreamAsync(string text, string targetLanguage, Func<string, Task> onChunk, CancellationToken ct = default)
    {
        // Back-compat overload: no explicit source; delegate to new overload with sourceLanguage = null (auto-detect)
        await TranslateStreamAsync(text, targetLanguage, (string?)null, onChunk, ct);
    }

    public async Task TranslateStreamAsync(string text, string targetLanguage, string? sourceLanguage, Func<string, Task> onChunk, CancellationToken ct = default)
    {
        // Detect source language when missing or set to 'auto'
        var src = string.IsNullOrWhiteSpace(sourceLanguage) || sourceLanguage!.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? await DetectLanguageNameAsync(text, ct)
            : sourceLanguage!;

        var sw = Stopwatch.StartNew();
        var provider = ResolveProvider(_aiSettings.TranslationProvider);
        var cur = _options.CurrentValue;
        var tcfg = _telemetryOptions.CurrentValue;
        using var activity = _telemetry.StartActivity(tcfg.Activities.TextTranslateStream, ActivityKind.Internal, a =>
        {
            a?.SetTag(tcfg.Tags.Provider, provider.Name);
            a?.SetTag(tcfg.Tags.TargetLanguage, targetLanguage);
            a?.SetTag("source.language", src);
            a?.SetTag(tcfg.Tags.InputLength, text?.Length ?? 0);
            a?.SetTag(tcfg.Tags.Streaming, true);
        });
        try
        {
            _requests.Add(1, new KeyValuePair<string, object?>(tcfg.Tags.Provider, provider.Name));
            var toTranslate = text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(src))
            {
                toTranslate = $"[Source language: {src}]\n\n" + toTranslate;
            }
            await provider.TranslateStreamAsync(toTranslate, targetLanguage, _aiSettings.TranslationModel ?? cur.DefaultModel, cur.Temperature, cur.MaxTokens, onChunk, ct);
        }
        catch (Exception ex)
        {
            var tags = _telemetryOptions.CurrentValue.Tags;
            activity?.SetTag(tags.Error, true);
            activity?.SetTag(tags.ExceptionType, ex.GetType().FullName);
            activity?.SetTag(tags.ExceptionMessage, ex.Message);
            // Fallback: non-streaming translate then chunk by words
            var full = await provider.TranslateAsync(text, targetLanguage, _aiSettings.TranslationModel ?? cur.DefaultModel, cur.Temperature, cur.MaxTokens, ct);
            full = Sanitize(full);
            var words = full.Split(' ');
            foreach (var w in words)
            {
                await onChunk((w.Length > 0 ? w : "") + " ");
            }
        }
        finally
        {
            sw.Stop();
            _latency.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>(_telemetryOptions.CurrentValue.Tags.Provider, provider.Name));
        }
    }
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        var provider = ResolveProvider(_aiSettings.GenerationProvider);
        return provider.ListModelsAsync(ct);
    }

    private static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var s = text;
        // Remove common reasoning blocks emitted by some models
        s = RemoveTagBlock(s, "think");
        s = RemoveTagBlock(s, "reasoning");
        s = RemoveTagBlock(s, "scratchpad");
        // Also strip XML comments if they wrap hidden thoughts
        s = RemoveXmlComments(s);
        return s.Trim();
    }

    private static string RemoveTagBlock(string input, string tag)
    {
        if (string.IsNullOrEmpty(input)) return input;
        string startTag = "<" + tag;
        string endTag = "</" + tag + ">";
        int searchFrom = 0;
        while (true)
        {
            int start = input.IndexOf(startTag, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (start < 0) break;
            int gt = input.IndexOf('>', start);
            if (gt < 0) { input = input[..start]; break; }
            int end = input.IndexOf(endTag, gt + 1, StringComparison.OrdinalIgnoreCase);
            int removeEnd = end >= 0 ? end + endTag.Length : input.Length;
            input = input.Remove(start, removeEnd - start);
            searchFrom = start; // continue searching in case multiple blocks
        }
        return input;
    }

    private static string RemoveXmlComments(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        const string start = "<!--";
        const string end = "-->";
        int searchFrom = 0;
        while (true)
        {
            int a = input.IndexOf(start, searchFrom, StringComparison.Ordinal);
            if (a < 0) break;
            int b = input.IndexOf(end, a + start.Length, StringComparison.Ordinal);
            int removeEnd = b >= 0 ? b + end.Length : input.Length;
            input = input.Remove(a, removeEnd - a);
            searchFrom = a;
        }
        return input;
    }
}