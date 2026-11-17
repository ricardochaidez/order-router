using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Entities.Routing;

/// <summary>
/// Everything the routing decided for one order, with the evidence an audit needs: the outcome, the routed
/// lines, the unfulfilled lines, the experiment assignment, the tie-break and how the plan was searched
/// (ADR-0001, AB-04). Immutable. The decision log (OR-021), diagnostics (OR-022) and storage (OR-027) read it.
/// </summary>
public sealed record RoutingDecision
{
    /// <summary>Creates a decision.</summary>
    /// <param name="status">The outcome.</param>
    /// <param name="routedLines">Lines assigned to suppliers, in request order; empty when nothing is routed.</param>
    /// <param name="unfulfilled">Lines not routed, in request order; empty when none.</param>
    /// <param name="assignment">The experiment assignment that produced the policies.</param>
    /// <param name="tieBreak">The key-5 result; <see langword="null"/> when no search ran.</param>
    /// <param name="searchMode">Exact or greedy; <see langword="null"/> when no search ran.</param>
    /// <param name="fallbackReason">Why greedy ran; <see langword="null"/> when the search was exact or did not run.</param>
    /// <exception cref="DomainValidationException">
    /// Routed lines are present only when the outcome has a plan (<see cref="RoutingOutcomeStatus.Routed"/> or
    /// <see cref="RoutingOutcomeStatus.PartiallyRouted"/>), or the search details are inconsistent.
    /// </exception>
    public RoutingDecision(
        RoutingOutcomeStatus status,
        IReadOnlyList<RoutedLine>? routedLines,
        IReadOnlyList<UnfulfilledLine>? unfulfilled,
        ExperimentAssignment assignment,
        TieBreakResult? tieBreak,
        SearchMode? searchMode,
        SearchFallbackReason? fallbackReason)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        var routed = routedLines ?? [];
        var notRouted = unfulfilled ?? [];
        var errors = new List<string>();

        if (!Enum.IsDefined(status))
        {
            errors.Add($"status {status} is not defined");
        }
        else if ((status is RoutingOutcomeStatus.NotRouted) != (routed.Count == 0))
        {
            errors.Add($"status {status} does not match {routed.Count} routed line(s)");
        }

        if (status is RoutingOutcomeStatus.Routed && notRouted.Count > 0)
        {
            errors.Add("a routed decision cannot have unfulfilled lines");
        }

        if (status is RoutingOutcomeStatus.PartiallyRouted && notRouted.Count == 0)
        {
            errors.Add("a partially routed decision needs unfulfilled lines");
        }

        if (status is not RoutingOutcomeStatus.NotRouted && tieBreak is null)
        {
            errors.Add("a decision with routed lines needs the tie-break result");
        }

        if ((tieBreak is null) != (searchMode is null))
        {
            errors.Add("the tie-break and the search mode are present together or not at all");
        }

        if (fallbackReason is not null && searchMode != Models.PlanSearch.SearchMode.Greedy)
        {
            errors.Add("a fallback reason needs the greedy search mode");
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException($"Invalid routing decision: {string.Join("; ", errors)}.");
        }

        Status = status;
        RoutedLines = routed;
        Unfulfilled = notRouted;
        Assignment = assignment;
        TieBreak = tieBreak;
        SearchMode = searchMode;
        FallbackReason = fallbackReason;
    }

    /// <summary>The outcome.</summary>
    public RoutingOutcomeStatus Status { get; }

    /// <summary>Lines assigned to suppliers, in request order; never null.</summary>
    public IReadOnlyList<RoutedLine> RoutedLines { get; }

    /// <summary>Lines not routed, in request order; never null.</summary>
    public IReadOnlyList<UnfulfilledLine> Unfulfilled { get; }

    /// <summary>The experiment assignment (kept for the decision log).</summary>
    public ExperimentAssignment Assignment { get; }

    /// <summary>Key-5 result; null when no search ran (nothing was demanded).</summary>
    public TieBreakResult? TieBreak { get; }

    /// <summary>How the plan was searched; null when no search ran (feeds <c>search_mode</c>, OR-022).</summary>
    public SearchMode? SearchMode { get; }

    /// <summary>Why greedy ran; null when exact or when no search ran.</summary>
    public SearchFallbackReason? FallbackReason { get; }
}
