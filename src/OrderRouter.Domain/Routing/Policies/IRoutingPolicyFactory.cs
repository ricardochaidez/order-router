using OrderRouter.Domain.Routing.Experiments;

namespace OrderRouter.Domain.Routing.Policies;

/// <summary>Builds the policies for one request from its experiment assignment (ADR-0001).</summary>
public interface IRoutingPolicyFactory
{
    /// <summary>Creates the policies.</summary>
    /// <param name="assignment">Arms and parameters.</param>
    RoutingPolicy Create(ExperimentAssignment assignment);
}
