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
        var sw = Stopwatch.StartNew();
        var provider = ResolveProvider(_aiSettings.TranslationProvider);
        var cur = _options.CurrentValue;
        var tcfg = _telemetryOptions.CurrentValue;
        using var activity = _telemetry.StartActivity(tcfg.Activities.TextTranslate, ActivityKind.Internal, a =>
        {
            a?.SetTag(tcfg.Tags.Provider, provider.Name);
            a?.SetTag(tcfg.Tags.TargetLanguage, targetLanguage);
            a?.SetTag(tcfg.Tags.InputLength, text?.Length ?? 0);
        });
        try
        {
            _requests.Add(1, new KeyValuePair<string, object?>(tcfg.Tags.Provider, provider.Name));
            var result = await provider.TranslateAsync(text, targetLanguage, _aiSettings.TranslationModel ?? cur.DefaultModel, cur.Temperature, cur.MaxTokens, ct);
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
        var sw = Stopwatch.StartNew();
        var provider = ResolveProvider(_aiSettings.TranslationProvider);
        var cur = _options.CurrentValue;
        var tcfg = _telemetryOptions.CurrentValue;
        using var activity = _telemetry.StartActivity(tcfg.Activities.TextTranslateStream, ActivityKind.Internal, a =>
        {
            a?.SetTag(tcfg.Tags.Provider, provider.Name);
            a?.SetTag(tcfg.Tags.TargetLanguage, targetLanguage);
            a?.SetTag(tcfg.Tags.InputLength, text?.Length ?? 0);
            a?.SetTag(tcfg.Tags.Streaming, true);
        });
        try
        {
            _requests.Add(1, new KeyValuePair<string, object?>(tcfg.Tags.Provider, provider.Name));
            await provider.TranslateStreamAsync(text, targetLanguage, _aiSettings.TranslationModel ?? cur.DefaultModel, cur.Temperature, cur.MaxTokens, onChunk, ct);
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