using Microsoft.Extensions.Options;
using OrderRouter.Domain.Sections;
using OrderRouter.Infrastructure.Mappers;

namespace OrderRouter.Infrastructure.Services;

/// <summary>
/// The <see cref="IValidateOptions{TOptions}"/> view of <see cref="ExperimentMappingExtensions"/>.
/// </summary>
/// <remarks>
/// Deliberately <b>not</b> registered with the options system. The OR-016 spike
/// (<c>OptionsMonitorReloadSpikeTests</c>) showed that a registered validator that fails on reload makes
/// <see cref="IOptionsMonitor{TOptions}"/> throw inside its own change callback, on the file-watcher thread, without
/// telling listeners. The startup check and the assigner call this class directly instead, so an invalid edit is
/// rejected and the last-known-good snapshot stays (ADR-0005).
/// </remarks>
public sealed class ExperimentsOptionsValidator : IValidateOptions<ExperimentsOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ExperimentsOptions options)
        => ExperimentMappingExtensions.TryBuild(options, out _, out var errors)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
}
