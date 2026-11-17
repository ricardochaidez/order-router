using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Sections;
using OrderRouter.Infrastructure.Mappers;

namespace OrderRouter.Infrastructure.Services;

/// <summary>
/// Assigns arms from reloadable configuration (SC-25, SC-26, ADR-0005). It holds one immutable snapshot. A valid
/// change swaps the snapshot atomically; an invalid change keeps the last-known-good one and logs an error, so a bad
/// edit never reaches a request and <see cref="Assign"/> never throws because of configuration.
/// </summary>
public sealed partial class ConfigurationExperimentAssigner : IExperimentAssigner, IDisposable
{
    private readonly ILogger<ConfigurationExperimentAssigner> logger;
    private readonly IDisposable? subscription;
    private ExperimentConfiguration snapshot;

    /// <summary>Builds the first snapshot and starts listening for changes.</summary>
    /// <param name="monitor">The reloadable options.</param>
    /// <param name="logger">Receives the applied and rejected events.</param>
    /// <exception cref="OptionsValidationException">The initial configuration is invalid (the host must not start).</exception>
    public ConfigurationExperimentAssigner(IOptionsMonitor<ExperimentsOptions> monitor, ILogger<ConfigurationExperimentAssigner> logger)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;

        if (!ExperimentMappingExtensions.TryBuild(monitor.CurrentValue, out var initial, out var errors))
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(ExperimentsOptions), errors);
        }

        snapshot = initial;
        subscription = monitor.OnChange(OnChanged);
    }

    /// <inheritdoc />
    public ExperimentAssignment Assign(AssignmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Volatile.Read(ref snapshot).Assign(context.CallerId, context.CustomerZip, context.OrderId);
    }

    /// <summary>Stops listening for configuration changes.</summary>
    public void Dispose() => subscription?.Dispose();

    private void OnChanged(ExperimentsOptions options, string? name)
    {
        if (ExperimentMappingExtensions.TryBuild(options, out var next, out var errors))
        {
            Volatile.Write(ref snapshot, next);
            ExperimentsConfigApplied(next.ConfigVersion);

            return;
        }

        // AB-03: the last-known-good snapshot keeps serving. The messages never contain caller values (NFR-11).
        ExperimentsConfigRejected(Volatile.Read(ref snapshot).ConfigVersion, string.Join(" | ", errors));
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Experiment configuration applied. config_version={ConfigVersion}")]
    private partial void ExperimentsConfigApplied(string configVersion);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Experiment configuration rejected; keeping the last known good config_version={KeptConfigVersion}. Errors: {Errors}")]
    private partial void ExperimentsConfigRejected(string keptConfigVersion, string errors);
}
