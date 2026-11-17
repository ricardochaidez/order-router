using OrderRouter.Domain.Models.PlanSearch;

namespace OrderRouter.Domain.Models;

/// <summary>
/// The outcome of key 5: the chosen plan plus the candidates it was chosen from. It feeds the decision log
/// so an audit can see which plans tied and which one won (AB-04). Immutable.
/// </summary>
public sealed record TieBreakResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="chosen">The plan picked; must be one of <paramref name="tiedCandidates"/>.</param>
    /// <param name="tiedCandidates">Every plan that was tied, in input order; at least one. Copied, so later changes by the caller do not leak in.</param>
    /// <exception cref="DomainValidationException">No candidates, or the chosen plan is not among them.</exception>
    public TieBreakResult(RoutingPlan chosen, IReadOnlyList<RoutingPlan> tiedCandidates)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(tiedCandidates);

        if (tiedCandidates.Count == 0)
        {
            throw new DomainValidationException("Tied candidates must contain at least one plan.");
        }

        if (!tiedCandidates.Contains(chosen))
        {
            throw new DomainValidationException("The chosen plan must be one of the tied candidates.");
        }

        Chosen = chosen;
        TiedCandidates = tiedCandidates.ToArray();
    }

    /// <summary>The plan to route to.</summary>
    public RoutingPlan Chosen { get; }

    /// <summary>Every plan that was tied, in input order. Never null or empty.</summary>
    public IReadOnlyList<RoutingPlan> TiedCandidates { get; }

    /// <summary>True when key 5 had a real choice, that is, more than one plan was tied (NFR-08d). With the core (control) tie-breaker that choice is deterministic; only the EXP-4 treatment makes it random.</summary>
    public bool UsedRandom => TiedCandidates.Count > 1;
}
