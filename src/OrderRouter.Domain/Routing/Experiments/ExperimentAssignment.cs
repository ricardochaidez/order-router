namespace OrderRouter.Domain.Routing.Experiments;

/// <summary>
/// The arms and parameters for one request. OR-016 adds the config version and exposure reasons (ADR-0005).
/// </summary>
public sealed record ExperimentAssignment
{
    /// <summary>Creates an assignment.</summary>
    /// <param name="arms">Arm per experiment.</param>
    /// <param name="parameters">Policy parameters.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ExperimentAssignment(ExperimentArms arms, RoutingParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(arms);
        ArgumentNullException.ThrowIfNull(parameters);

        Arms = arms;
        Parameters = parameters;
    }

    /// <summary>Every experiment on control with default parameters (AB-03).</summary>
    public static ExperimentAssignment AllControl { get; } = new(ExperimentArms.AllControl, RoutingParameters.Default);

    /// <summary>Arm per experiment.</summary>
    public ExperimentArms Arms { get; }

    /// <summary>Policy parameters.</summary>
    public RoutingParameters Parameters { get; }
}
