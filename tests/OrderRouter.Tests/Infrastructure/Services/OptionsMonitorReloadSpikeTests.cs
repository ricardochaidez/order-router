using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace OrderRouter.Tests.Infrastructure.Services;

/// <summary>
/// OR-016 Phase 3 spike (ADR-0005): what does <see cref="IOptionsMonitor{TOptions}"/> do when a reload fails an
/// <see cref="IValidateOptions{TOptions}"/> check? The result decides where reload validation lives.
/// </summary>
public class OptionsMonitorReloadSpikeTests
{
    private sealed class SpikeOptions
    {
        public int Percentage { get; set; }
    }

    private sealed class SpikeValidator : IValidateOptions<SpikeOptions>
    {
        public ValidateOptionsResult Validate(string? name, SpikeOptions options)
            => options.Percentage is < 0 or > 100 ? ValidateOptionsResult.Fail("percentage out of range") : ValidateOptionsResult.Success;
    }

    [Fact]
    public void Reload_WhenARegisteredValidatorFails_TheExceptionEscapesIntoTheReloadCaller()
    {
        // Findings: the monitor re-reads and validates inside its own change callback, so the failure is thrown on
        // the thread that raised the reload (in production: the file watcher thread) and our OnChange listener is
        // never told. Validation therefore must not be registered on the monitor for the reloadable options.
        var root = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Percentage"] = "10" }).Build();
        var services = new ServiceCollection();
        services.AddOptions<SpikeOptions>().Bind(root);
        services.AddSingleton<IValidateOptions<SpikeOptions>, SpikeValidator>();
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<SpikeOptions>>();
        var notified = 0;
        using var subscription = monitor.OnChange((_, _) => notified++);

        root["Percentage"] = "150";

        var exception = Record.Exception(() => root.Reload());

        Assert.Equal(0, notified);
        Assert.NotNull(exception);
        Assert.Contains("OptionsValidationException", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Reload_WithoutARegisteredValidator_NotifiesTheListenerWithTheRawValue()
    {
        var root = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Percentage"] = "10" }).Build();
        var services = new ServiceCollection();
        services.AddOptions<SpikeOptions>().Bind(root);
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<SpikeOptions>>();
        var seen = new List<int>();
        using var subscription = monitor.OnChange((options, _) => seen.Add(options.Percentage));

        root["Percentage"] = "150";
        root.Reload();

        Assert.Equal([150], seen);
    }
}
