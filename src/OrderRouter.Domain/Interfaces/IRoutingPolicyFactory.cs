using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Entities.Routing;

namespace OrderRouter.Domain.Interfaces;

/// <summary>Builds the policies for one request from its experiment assignment (ADR-0001).</summary>
public interface IRoutingPolicyFactory
{
    /// <summary>Creates the policies.</summary>
    /// <param name="assignment">Arms and parameters.</param>
    RoutingPolicy Create(ExperimentAssignment assignment);
}
