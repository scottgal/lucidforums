using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LucidForums.Models.Entities;
using LucidForums.Services.Ai;
using LucidForums.Services.Observability;
using LucidForums.Tests.TestDoubles;
using Microsoft.Extensions.Options;
using Xunit;

namespace LucidForums.Tests;

public class TextAiServiceTests
{
    private static (TextAiService svc, FakeTelemetry tel, TestOptionsMonitor<AiOptions> ai, TestOptionsMonitor<TelemetryOptions> topts, FakeChatProvider[] providers) Create(
        string? providerNameOption,
        params string[] providerNames)
    {
        var providers = providerNames.Select(n => new FakeChatProvider(n)).ToArray();
        var ai = new TestOptionsMonitor<AiOptions>(new AiOptions
        {
            Provider = providerNameOption,
            DefaultModel = "llama3",
            Temperature = 0.42,
            MaxTokens = 777
        });
        var tel = new FakeTelemetry();
        var topts = new TestOptionsMonitor<TelemetryOptions>(new TelemetryOptions());
        var svc = new TextAiService(providers, ai, tel, topts);
        return (svc, tel, ai, topts, providers);
    }

    [Fact]
    public async Task GenerateAsync_Uses_Explicit_Provider_When_Matches()
    {
        var (svc, tel, ai, topts, providers) = Create("lmstudio", "ollama", "lmstudio");
        var charter = new Charter { Name = "Test" };
        var output = await svc.GenerateAsync(charter, "hi", null, CancellationToken.None);

        output.Should().Be(providers[1].GenerateResult);
        providers[1].GenerateCalls.Should().HaveCount(1);
        providers[0].GenerateCalls.Should().BeEmpty();

        // Activity started with tags
        tel.StartedActivities.Should().NotBeEmpty();
        var act = tel.StartedActivities.Last();
        act.OperationName.Should().Be(topts.CurrentValue.Activities.TextGenerate);
        act.Tags.Should().Contain(t => t.Key == topts.CurrentValue.Tags.Provider && (string)t.Value! == providers[1].Name);
        act.Tags.Should().Contain(t => t.Key == topts.CurrentValue.Tags.Model && (string)t.Value! == ai.CurrentValue.DefaultModel);
        var hasInputLen = act.TagObjects.Any(t => t.Key == topts.CurrentValue.Tags.InputLength);
        hasInputLen.Should().BeTrue();

        // Metrics recorded
        tel.CounterAdds.ContainsKey(topts.CurrentValue.Metrics.TextRequestsCounter).Should().BeTrue();
        tel.HistogramRecords.ContainsKey(topts.CurrentValue.Metrics.TextRequestsLatencyHistogram).Should().BeTrue();
        tel.CounterAdds[topts.CurrentValue.Metrics.TextRequestsCounter].Last().Tags.Should().Contain(kv => kv.Key == topts.CurrentValue.Tags.Provider && (string)kv.Value! == providers[1].Name);
        tel.HistogramRecords[topts.CurrentValue.Metrics.TextRequestsLatencyHistogram].Last().Tags.Should().Contain(kv => kv.Key == topts.CurrentValue.Tags.Provider && (string)kv.Value! == providers[1].Name);
    }

    [Fact]
    public async Task GenerateAsync_Falls_Back_To_Ollama_Then_First()
    {
        // No matching provider name, should choose "ollama"
        var (svc1, _, _, _, providers1) = Create("unknown", "lmstudio", "ollama", "other");
        var res1 = await svc1.GenerateAsync(new Charter(), "x");
        providers1[1].GenerateCalls.Should().HaveCount(1);

        // If no ollama exists, pick first
        var (svc2, _, _, _, providers2) = Create("unknown", "foo", "bar");
        var res2 = await svc2.GenerateAsync(new Charter(), "x");
        providers2[0].GenerateCalls.Should().HaveCount(1);
    }

    [Fact]
    public async Task GenerateAsync_Passes_Model_And_Params()
    {
        var (svc, _, ai, _, providers) = Create("ollama", "ollama");
        var charter = new Charter();
        var customModel = "custom-model";
        _ = await svc.GenerateAsync(charter, "hello", customModel);
        var call = providers[0].GenerateCalls.Single();
        call.Model.Should().Be(customModel);
        call.Temperature.Should().Be(ai.CurrentValue.Temperature);
        call.MaxTokens.Should().Be(ai.CurrentValue.MaxTokens);
    }

    [Fact]
    public async Task GenerateAsync_Tags_Exception_And_Rethrows()
    {
        var (svc, tel, _, topts, providers) = Create("ollama", "ollama");
        providers[0].GenerateException = new InvalidOperationException("boom");
        var charter = new Charter();
        var actCount = tel.StartedActivities.Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GenerateAsync(charter, "test"));
        var act = tel.StartedActivities.Last();
        var hasError = act.TagObjects.Any(t => t.Key == topts.CurrentValue.Tags.Error && t.Value is bool b && b);
        hasError.Should().BeTrue();
        var hasType = act.TagObjects.Any(t => t.Key == topts.CurrentValue.Tags.ExceptionType && (t.Value?.ToString() ?? string.Empty).Contains("InvalidOperationException"));
        hasType.Should().BeTrue();
        var hasMsg = act.TagObjects.Any(t => t.Key == topts.CurrentValue.Tags.ExceptionMessage && (t.Value?.ToString() ?? string.Empty).Contains("boom"));
        hasMsg.Should().BeTrue();
    }

    [Fact]
    public async Task TranslateAsync_Basics()
    {
        var (svc, tel, ai, topts, providers) = Create("ollama", "ollama");
        var result = await svc.TranslateAsync("hello", "fr");
        result.Should().Be(providers[0].TranslateResult);
        var call = providers[0].TranslateCalls.Single();
        call.Model.Should().Be(ai.CurrentValue.DefaultModel);
        tel.CounterAdds[topts.CurrentValue.Metrics.TextRequestsCounter].Should().NotBeEmpty();
    }

    [Fact]
    public async Task TranslateStreamAsync_Uses_Provider_When_Success()
    {
        var (svc, tel, ai, topts, providers) = Create("ollama", "ollama");
        providers[0].TranslateResult = "one two";
        var chunks = new List<string>();
        await svc.TranslateStreamAsync("x", "es", s => { chunks.Add(s); return Task.CompletedTask; });
        providers[0].TranslateStreamCalls.Should().HaveCount(1);
        new string(chunks.SelectMany(c => c).ToArray()).Should().Be("one two ");
    }

    [Fact]
    public async Task TranslateStreamAsync_Fallbacks_To_NonStreaming_On_Exception()
    {
        var (svc, tel, ai, topts, providers) = Create("ollama", "ollama");
        providers[0].TranslateStreamException = new Exception("no stream");
        providers[0].TranslateResult = "hello world";
        var chunks = new List<string>();
        await svc.TranslateStreamAsync("input", "de", s => { chunks.Add(s); return Task.CompletedTask; });

        // Should have attempted streaming once and then used non-streaming TranslateAsync
        providers[0].TranslateStreamCalls.Should().HaveCount(1);
        providers[0].TranslateCalls.Should().HaveCount(1);
        string.Concat(chunks).Should().Be("hello world ");

        // Exception tagging should be present on activity
        var act = tel.StartedActivities.Last();
        var hasError = act.TagObjects.Any(t => t.Key == topts.CurrentValue.Tags.Error);
        hasError.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateAsync_Strips_Think_Blocks()
    {
        var (svc, _, _, _, providers) = Create("ollama", "ollama");
        providers[0].GenerateResult = "<think>Internal chain of thought that should not leak.</think>\n**Final answer**: Hello!";
        var charter = new Charter { Name = "Test" };
        var output = await svc.GenerateAsync(charter, "hi");
        output.Should().Be("**Final answer**: Hello!");
    }

    [Fact]
    public async Task GenerateAsync_Leaves_Normal_Text_Unchanged()
    {
        var (svc, _, _, _, providers) = Create("ollama", "ollama");
        providers[0].GenerateResult = "Just normal output.";
        var output = await svc.GenerateAsync(new Charter(), "x");
        output.Should().Be("Just normal output.");
    }
}