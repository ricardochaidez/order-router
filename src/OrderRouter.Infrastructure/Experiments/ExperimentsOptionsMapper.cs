using System.Diagnostics.CodeAnalysis;
using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing.Experiments;

namespace OrderRouter.Infrastructure.Experiments;

/// <summary>
/// Turns <see cref="ExperimentsOptions"/> into the immutable domain snapshot. The startup check and the reload path
/// both call this one mapper, so they can never disagree about what is valid. Every error is reported at once, and
/// no message echoes a caller value (NFR-11); ZIP values come from configuration and are fine to show.
/// </summary>
public static class ExperimentsOptionsMapper
{
    private const string exp1Id = nameof(ExperimentArms.Exp1ConsolidationVsQuality);
    private const string exp2Id = nameof(ExperimentArms.Exp2UnratedSuppliers);
    private const string exp3Id = nameof(ExperimentArms.Exp3PartialRouting);

    /// <summary>Tries to build a snapshot.</summary>
    /// <param name="options">The bound options.</param>
    /// <param name="configuration">The snapshot when valid; otherwise <c>null</c>.</param>
    /// <param name="errors">Every problem found; empty when valid.</param>
    /// <returns><c>true</c> when the options are valid.</returns>
    public static bool TryBuild(ExperimentsOptions options, [NotNullWhen(true)] out ExperimentConfiguration? configuration, out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(options);

        var found = new List<string>();

        if (options.BindError is not null)
        {
            found.Add(options.BindError);
        }

        var exp1 = BuildDefinition(exp1Id, "exp1", options.Experiments.Exp1ConsolidationVsQuality, found);
        var exp2 = BuildDefinition(exp2Id, "exp2", options.Experiments.Exp2UnratedSuppliers, found);
        var exp3 = BuildDefinition(exp3Id, "exp3", options.Experiments.Exp3PartialRouting, found);
        var parameters = BuildParameters(options, found);

        errors = found;
        configuration = null;

        if (found.Count > 0 || exp1 is null || exp2 is null || exp3 is null || parameters is null)
        {
            return false;
        }

        configuration = new ExperimentConfiguration(exp1, exp2, exp3, parameters, ConfigVersion.Compute(exp1, exp2, exp3, parameters));

        return true;
    }

    private static ExperimentDefinition? BuildDefinition(string id, string defaultSalt, ExperimentOptions? options, List<string> errors)
    {
        // AB-03: no section means the experiment does not run.
        if (options is null)
        {
            return ExperimentDefinition.Disabled(id, defaultSalt);
        }

        var path = $"{nameof(ExperimentsOptions.Experiments)}:{id}";
        var targeting = BuildTargeting(path, options.Targeting, errors);

        try
        {
            // Still checked when the targeting was invalid, so one edit reports every problem at once.
            var definition = new ExperimentDefinition(id, options.Enabled, options.TreatmentPercentage, options.Salt!, targeting ?? ExperimentTargeting.Everyone);

            return targeting is null ? null : definition;
        }
        catch (DomainValidationException exception)
        {
            errors.Add($"{path}: {exception.Message}");

            return null;
        }
    }

    private static ExperimentTargeting? BuildTargeting(string path, ExperimentTargetingOptions options, List<string> errors)
    {
        var ranges = new List<ZipRange>();
        var valid = true;

        foreach (var text in options.ZipRanges)
        {
            // ZipRange.TryParse is strict on purpose: 5 digits, no padding, so "2100-2199" is rejected (AC12).
            if (ZipRange.TryParse(text, out var range))
            {
                ranges.Add(range);
            }
            else
            {
                errors.Add($"{path}:Targeting:ZipRanges: \"{text}\" must be \"NNNNN\" or \"NNNNN-NNNNN\" (5 digits each, start <= end)");
                valid = false;
            }
        }

        try
        {
            var targeting = new ExperimentTargeting(options.Callers, options.ZipPrefixes, ranges);

            return valid ? targeting : null;
        }
        catch (DomainValidationException exception)
        {
            errors.Add($"{path}:Targeting: {exception.Message}");

            return null;
        }
    }

    private static RoutingParameters? BuildParameters(ExperimentsOptions options, List<string> errors)
    {
        try
        {
            return new RoutingParameters(
                options.Routing.SimilarityBand ?? RoutingParameters.Default.SimilarityBand,
                options.Experiments.Exp1ConsolidationVsQuality?.Parameters.QualityUpliftThreshold ?? RoutingParameters.DefaultQualityUpliftThreshold,
                options.Experiments.Exp2UnratedSuppliers?.Parameters.UnratedPrior ?? RoutingParameters.DefaultUnratedPrior);
        }
        catch (DomainValidationException exception)
        {
            errors.Add(exception.Message);

            return null;
        }
    }
}
