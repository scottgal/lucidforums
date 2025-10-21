using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LucidForums.Services.Observability;

public interface ITelemetry
{
    Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal, Action<Activity>? configure = null);
    Counter<long> GetCounter(string name);
    Histogram<double> GetHistogram(string name);
}

public static class TelemetryConstants
{
    public const string ActivitySourceName = "LucidForums.Services";
    public const string MeterName = "LucidForums.Services";
}

public sealed class Telemetry : ITelemetry
{
    private static readonly ActivitySource ActivitySource = new(TelemetryConstants.ActivitySourceName);
    private static readonly Meter Meter = new(TelemetryConstants.MeterName);
    private readonly ConcurrentDictionary<string, Counter<long>> _counters = new();
    private readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new();

    public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal, Action<Activity>? configure = null)
    {
        var activity = ActivitySource.StartActivity(name, kind);
        if (activity is not null && configure is not null)
        {
            try { configure(activity); } catch { /* no-op */ }
        }
        return activity;
    }

    public Counter<long> GetCounter(string name) => _counters.GetOrAdd(name, n => Meter.CreateCounter<long>(n));

    public Histogram<double> GetHistogram(string name) => _histograms.GetOrAdd(name, n => Meter.CreateHistogram<double>(n));
}
