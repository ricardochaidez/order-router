using OrderRouter.Domain.Routing.Experiments;

namespace OrderRouter.Application.Routing.Experiments;

/// <summary>
/// The vendor-agnostic port that decides the experiment arms for one request (SC-25, ADR-0005). Routing code
/// depends only on this and on <see cref="ExperimentAssignment"/>, so a Statsig or LaunchDarkly adapter can
/// replace the configuration adapter without touching it.
/// </summary>
public interface IExperimentAssigner
{
    /// <summary>
    /// Assigns the arms, parameters and exposure reasons. Synchronous and fail-safe: it never throws for a valid
    /// context, and falls back to control when configuration is missing or invalid (AB-03).
    /// </summary>
    /// <param name="context">The caller, customer ZIP and order ID.</param>
    ExperimentAssignment Assign(AssignmentContext context);
}
