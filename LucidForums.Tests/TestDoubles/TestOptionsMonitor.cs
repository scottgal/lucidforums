using System;
using Microsoft.Extensions.Options;

namespace LucidForums.Tests.TestDoubles;

public sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    private T _value;

    public TestOptionsMonitor(T value)
    {
        _value = value;
    }

    public T CurrentValue => _value;

    public T Get(string? name) => _value;

    public IDisposable OnChange(Action<T, string?> listener)
    {
        return new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }

    public void Set(T value) => _value = value;
}