using System.Text.Json;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Helpers;

/// <summary>
/// Builds the decision log entry from a routing decision (AB-04). It only reads the decision and copies values; it
/// holds no business rule. The raw caller ID is not an input, so it cannot reach the log (NFR-11).
/// </summary>
public static class RoutingDecisionLogEntryFactory
{
    /// <summary>Builds the entry.</summary>
    /// <param name="id">The stored order id.</param>
    /// <param name="orderId">The caller's order id.</param>
    /// <param name="callerHash">The hashed caller, or <see langword="null"/> when there is no caller ID.</param>
    /// <param name="customerZip">The delivery ZIP (5 digits).</param>
    /// <param name="decision">The routing decision.</param>
    public static RoutingDecisionLogEntry Create(Guid id, string orderId, string? callerHash, string customerZip, RoutingDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var arms = decision.Assignment.Arms;
        var reasons = decision.Assignment.Reasons;

        // Suppliers in order of their first line, so the text is stable for one decision. FR-09: one mode per supplier per order.
        var suppliers = decision.RoutedLines
            .GroupBy(line => line.Supplier.Id, StringComparer.Ordinal)
            .Select(group => (group.First().Supplier, group.First().Mode))
            .ToArray();

        // Key 5 ran with more than one tied plan: record all of them and the pick (AC2). No random, nothing to list.
        var tie = decision.TieBreak is { UsedRandom: true } result ? result : null;

        return new RoutingDecisionLogEntry(
            id,
            orderId,
            callerHash,
            Text(arms.Exp1ConsolidationVsQuality),
            Text(reasons.Exp1ConsolidationVsQuality),
            Text(arms.Exp2UnratedSuppliers),
            Text(reasons.Exp2UnratedSuppliers),
            Text(arms.Exp3PartialRouting),
            Text(reasons.Exp3PartialRouting),
            Text(arms.Exp4TieBreaking),
            Text(reasons.Exp4TieBreaking),
            string.Join(',', suppliers.Select(entry => $"{entry.Supplier.Id}:{Text(entry.Mode)}")),
            tie is null ? null : string.Join(',', tie.TiedCandidates.Select(PlanText)),
            tie is null ? null : PlanText(tie.Chosen),
            suppliers.Length,
            // Quality describes the routed plan. When all-or-nothing held every line back there is no routed plan.
            suppliers.Length > 0 ? decision.TieBreak?.Chosen.Quality : null,
            suppliers.Count(entry => entry.Supplier.Rating is null),
            decision.Unfulfilled.Count,
            decision.SearchMode is { } mode ? Text(mode) : null,
            decision.Assignment.ConfigVersion,
            customerZip);
    }

    // A plan is written as its distinct suppliers, so two plans that differ only in the category split stay readable.
    private static string PlanText(RoutingPlan plan)
        => string.Join('+', plan.Assignments.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static string Text<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
