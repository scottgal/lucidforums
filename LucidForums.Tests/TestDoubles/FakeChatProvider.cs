using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LucidForums.Models.Entities;
using LucidForums.Services.Ai;

namespace LucidForums.Tests.TestDoubles;

public sealed class FakeChatProvider : IChatProvider
{
    public string Name { get; }

    public record GenerateCall(Charter Charter, string UserInput, string? Model, double? Temperature, int? MaxTokens);
    public record TranslateCall(string Text, string TargetLanguage, string? Model, double? Temperature, int? MaxTokens);
    public record TranslateStreamCall(string Text, string TargetLanguage, string? Model, double? Temperature, int? MaxTokens);

    public List<GenerateCall> GenerateCalls { get; } = new();
    public List<TranslateCall> TranslateCalls { get; } = new();
    public List<TranslateStreamCall> TranslateStreamCalls { get; } = new();

    public Exception? GenerateException { get; set; }
    public Exception? TranslateException { get; set; }
    public Exception? TranslateStreamException { get; set; }

    public string GenerateResult { get; set; } = "generated";
    public string TranslateResult { get; set; } = "translated";

    public FakeChatProvider(string name)
    {
        Name = name;
    }

    public Task<string> GenerateAsync(Charter charter, string userInput, string? model, double? temperature, int? maxTokens, CancellationToken ct)
    {
        GenerateCalls.Add(new GenerateCall(charter, userInput, model, temperature, maxTokens));
        if (GenerateException != null) throw GenerateException;
        return Task.FromResult(GenerateResult);
    }

    public Task<string> TranslateAsync(string text, string targetLanguage, string? model, double? temperature, int? maxTokens, CancellationToken ct)
    {
        TranslateCalls.Add(new TranslateCall(text, targetLanguage, model, temperature, maxTokens));
        if (TranslateException != null) throw TranslateException;
        return Task.FromResult(TranslateResult);
    }

    public Task TranslateStreamAsync(string text, string targetLanguage, string? model, double? temperature, int? maxTokens, Func<string, Task> onChunk, CancellationToken ct)
    {
        TranslateStreamCalls.Add(new TranslateStreamCall(text, targetLanguage, model, temperature, maxTokens));
        if (TranslateStreamException != null) throw TranslateStreamException;
        // default: stream by words quickly
        var words = (TranslateResult ?? string.Empty).Split(' ');
        foreach (var w in words)
        {
            onChunk((w.Length > 0 ? w : "") + " ");
        }
        return Task.CompletedTask;
    }
}