using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using LucidForums.Services.Observability;

namespace LucidForums.Tests.TestDoubles;

public sealed class FakeTelemetry : ITelemetry, IDisposable
{
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;

    public List<Activity> StartedActivities { get; } = new();

    public ConcurrentDictionary<string, List<(double Value, KeyValuePair<string, object?>[] Tags)>> HistogramRecords { get; } = new();
    public ConcurrentDictionary<string, List<(long Value, KeyValuePair<string, object?>[] Tags)>> CounterAdds { get; } = new();

    private readonly ConcurrentDictionary<string, Counter<long>> _counters = new();
    private readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new();

    private readonly MeterListener _listener;

    public FakeTelemetry(string name = "Test")
    {
        _activitySource = new ActivitySource(name + ".Activities");
        _meter = new Meter(name + ".Metrics");

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter == _meter)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var list = CounterAdds.GetOrAdd(instrument.Name, _ => new());
            list.Add((measurement, ToArray(tags)));
        });
        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            var list = HistogramRecords.GetOrAdd(instrument.Name, _ => new());
            list.Add((measurement, ToArray(tags)));
        });
        _listener.Start();
    }

    public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal, Action<Activity>? configure = null)
    {
        var activity = _activitySource.StartActivity(name, kind) ?? new Activity(name);
        activity.Start();
        try { configure?.Invoke(activity); } catch { /* no-op */ }
        StartedActivities.Add(activity);
        return activity;
    }

    public Counter<long> GetCounter(string name)
    {
        return _counters.GetOrAdd(name, n => _meter.CreateCounter<long>(n));
    }

    public Histogram<double> GetHistogram(string name)
    {
        return _histograms.GetOrAdd(name, n => _meter.CreateHistogram<double>(n));
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meter.Dispose();
        _activitySource.Dispose();
    }

    private static KeyValuePair<string, object?>[] ToArray(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var arr = new KeyValuePair<string, object?>[tags.Length];
        for (int i = 0; i < tags.Length; i++) arr[i] = tags[i];
        return arr;
    }
}