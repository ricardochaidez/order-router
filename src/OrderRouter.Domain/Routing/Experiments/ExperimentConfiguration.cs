using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Domain.Routing.Experiments;

/// <summary>
/// An immutable snapshot of all experiment settings: the three definitions, the routing parameters and the
/// version that identifies them. Swapping one snapshot for another is atomic, so a request never sees half a config.
/// </summary>
public sealed record ExperimentConfiguration
{
    /// <summary>Creates a snapshot.</summary>
    /// <param name="exp1ConsolidationVsQuality">EXP-1 definition.</param>
    /// <param name="exp2UnratedSuppliers">EXP-2 definition.</param>
    /// <param name="exp3PartialRouting">EXP-3 definition.</param>
    /// <param name="parameters">Policy parameters carried to every assignment.</param>
    /// <param name="configVersion">Non-empty version label of this snapshot.</param>
    /// <exception cref="ArgumentException">The version is blank.</exception>
    public ExperimentConfiguration(
        ExperimentDefinition exp1ConsolidationVsQuality,
        ExperimentDefinition exp2UnratedSuppliers,
        ExperimentDefinition exp3PartialRouting,
        RoutingParameters parameters,
        string configVersion)
    {
        ArgumentNullException.ThrowIfNull(exp1ConsolidationVsQuality);
        ArgumentNullException.ThrowIfNull(exp2UnratedSuppliers);
        ArgumentNullException.ThrowIfNull(exp3PartialRouting);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(configVersion);

        Exp1ConsolidationVsQuality = exp1ConsolidationVsQuality;
        Exp2UnratedSuppliers = exp2UnratedSuppliers;
        Exp3PartialRouting = exp3PartialRouting;
        Parameters = parameters;
        ConfigVersion = configVersion;
    }

    /// <summary>EXP-1 definition.</summary>
    public ExperimentDefinition Exp1ConsolidationVsQuality { get; }

    /// <summary>EXP-2 definition.</summary>
    public ExperimentDefinition Exp2UnratedSuppliers { get; }

    /// <summary>EXP-3 definition.</summary>
    public ExperimentDefinition Exp3PartialRouting { get; }

    /// <summary>Policy parameters carried to every assignment.</summary>
    public RoutingParameters Parameters { get; }

    /// <summary>Version label of this snapshot (AB-04).</summary>
    public string ConfigVersion { get; }

    /// <summary>Assigns the arms for one request. Each experiment is evaluated on its own, so arms are independent (AB-01).</summary>
    /// <param name="callerId">The authenticated caller; <c>null</c> or blank means unknown.</param>
    /// <param name="zip">The customer ZIP.</param>
    /// <param name="orderId">The order ID, used as the unit when there is no caller.</param>
    public ExperimentAssignment Assign(string? callerId, ZipCode zip, string orderId)
    {
        ArgumentNullException.ThrowIfNull(orderId);

        // A blank caller counts as missing, the same as no claim at all.
        var caller = string.IsNullOrWhiteSpace(callerId) ? null : callerId;

        var exp1 = Exp1ConsolidationVsQuality.Evaluate(caller, zip, orderId);
        var exp2 = Exp2UnratedSuppliers.Evaluate(caller, zip, orderId);
        var exp3 = Exp3PartialRouting.Evaluate(caller, zip, orderId);

        return new ExperimentAssignment(
            new ExperimentArms(exp1.Arm, exp2.Arm, exp3.Arm),
            Parameters,
            new ExperimentReasons(exp1.Reason, exp2.Reason, exp3.Reason),
            ConfigVersion);
    }
}
