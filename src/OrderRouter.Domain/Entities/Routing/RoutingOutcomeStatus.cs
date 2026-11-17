namespace OrderRouter.Domain.Entities.Routing;

/// <summary>Result of the unfulfillable policy. OR-015 maps it to the v1 response status.</summary>
public enum RoutingOutcomeStatus
{
    /// <summary>Every line is routed.</summary>
    Routed,

    /// <summary>Some lines are routed and some are not (treatment arm of EXP-3).</summary>
    PartiallyRouted,

    /// <summary>Nothing is routed.</summary>
    NotRouted,
}
