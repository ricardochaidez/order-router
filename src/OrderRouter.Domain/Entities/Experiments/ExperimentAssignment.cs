namespace OrderRouter.Domain.Entities.Experiments;

/// <summary>
/// The arms and parameters for one request, with the reason for each arm and the version of the configuration
/// that produced them (ADR-0005, AB-04).
/// </summary>
public sealed record ExperimentAssignment
{
    /// <summary>The <see cref="ConfigVersion"/> of an assignment that came from no configuration.</summary>
    public const string DefaultConfigVersion = "defaults";

    /// <summary>Creates an assignment.</summary>
    /// <param name="arms">Arm per experiment.</param>
    /// <param name="parameters">Policy parameters.</param>
    /// <param name="reasons">Reason per experiment; defaults to all <see cref="ExposureReason.Disabled"/>.</param>
    /// <param name="configVersion">Non-empty version of the configuration; defaults to <see cref="DefaultConfigVersion"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">The version is blank.</exception>
    public ExperimentAssignment(ExperimentArms arms, RoutingParameters parameters, ExperimentReasons? reasons = null, string configVersion = DefaultConfigVersion)
    {
        ArgumentNullException.ThrowIfNull(arms);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(configVersion);

        Arms = arms;
        Parameters = parameters;
        Reasons = reasons ?? ExperimentReasons.AllDisabled;
        ConfigVersion = configVersion;
    }

    /// <summary>Every experiment on control with default parameters (AB-03); used by tests and as a fallback.</summary>
    public static ExperimentAssignment AllControl { get; } = new(ExperimentArms.AllControl, RoutingParameters.Default);

    /// <summary>Arm per experiment.</summary>
    public ExperimentArms Arms { get; }

    /// <summary>Policy parameters.</summary>
    public RoutingParameters Parameters { get; }

    /// <summary>Why each experiment got its arm.</summary>
    public ExperimentReasons Reasons { get; }

    /// <summary>Version of the configuration that produced this assignment.</summary>
    public string ConfigVersion { get; }
}
