using Microsoft.Extensions.Options;

namespace OrderRouter.Tests.TestData;

/// <summary>An <see cref="IOptionsMonitor{TOptions}"/> that tests can push values into, like a file reload would.</summary>
internal sealed class TestOptionsMonitor<TOptions>(TOptions initial) : IOptionsMonitor<TOptions>
{
    private readonly List<Action<TOptions, string?>> listeners = [];

    public TOptions CurrentValue { get; private set; } = initial;

    public TOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<TOptions, string?> listener)
    {
        listeners.Add(listener);

        return new Subscription(() => listeners.Remove(listener));
    }

    /// <summary>Sets the current value and notifies every listener.</summary>
    public void Change(TOptions value)
    {
        CurrentValue = value;

        foreach (var listener in listeners.ToArray())
        {
            listener(value, Options.DefaultName);
        }
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
