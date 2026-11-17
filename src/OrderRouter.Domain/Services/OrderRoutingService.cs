using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Models.PlanSearch;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Services;

/// <summary>
/// The whole routing pipeline as one pure, synchronous domain service (ADR-0001): eligibility, candidates,
/// plan search, ranking keys 2-4, key-5 tie-break and the unfulfillable policy. It does no I/O; the Application
/// layer loads the suppliers and passes them in, and the plan search is reached through a port.
/// </summary>
/// <param name="eligibility">Eligibility rules (OR-010).</param>
/// <param name="planSearch">Plan search port (OR-012).</param>
/// <param name="tieBreaker">Key 5 (OR-014).</param>
public sealed class OrderRoutingService(SupplierEligibility eligibility, IPlanSearchService planSearch, ITieBreaker tieBreaker)
{
    /// <summary>Routes an order.</summary>
    /// <param name="order">The order with products resolved.</param>
    /// <param name="suppliers">Suppliers that handle any category of the order; may be empty.</param>
    /// <param name="policy">The policies for this request.</param>
    /// <returns>The decision. Never null.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// A state that cannot happen when the rules hold: no plan despite an eligible supplier for every demanded
    /// category, or a supplier with two modes in one order. The code never invents or drops a plan (fail safe).
    /// </exception>
    public RoutingDecision Route(OrderToRoute order, IReadOnlyList<Supplier> suppliers, RoutingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(suppliers);
        ArgumentNullException.ThrowIfNull(policy);

        // Eligibility is a property of the category, so evaluate it once for each distinct known category.
        var evaluations = order.Lines
            .Where(line => line.Category is not null)
            .Select(line => line.Category!.Value)
            .Distinct()
            .ToDictionary(
                category => category,
                category => eligibility.Evaluate(category, order.CustomerZip, order.MailOrder, suppliers));

        var unfulfilled = new List<UnfulfilledLine>();
        var demandCounts = new Dictionary<Category, int>();

        foreach (var line in order.Lines)
        {
            if (line.Category is not { } category)
            {
                unfulfilled.Add(new UnfulfilledLine(line.ProductCode, line.Quantity, null, UnfulfilledLine.UnknownProductCode));
            }
            else if (evaluations[category].Reason is { } reason)
            {
                unfulfilled.Add(new UnfulfilledLine(line.ProductCode, line.Quantity, category, reason.Code()));
            }
            else
            {
                // FR-10, DEV-10: lines of one category (duplicates included) are fulfilled together.
                demandCounts[category] = demandCounts.GetValueOrDefault(category) + 1;
            }
        }

        // AC6: nothing to fulfill means nothing to search and nothing to tie-break.
        if (demandCounts.Count == 0)
        {
            return ToDecision(policy.Unfulfillable.Apply(null, unfulfilled), [], policy, null, null);
        }

        var offers = BuildOffers(evaluations, demandCounts.Keys);
        var candidates = offers.Values
            .Select(offer => new SupplierCandidate(
                offer.Supplier.Id,
                offer.Categories,
                offer.Mode,
                policy.Rating.EffectiveRating(offer.Supplier)))
            .ToList();

        var request = new PlanSearchRequest(
            demandCounts.Select(entry => new CategoryDemand(entry.Key, entry.Value)),
            candidates,
            policy.Assignment.Parameters.SimilarityBand,
            policy.Selection.MaxExtraShipments);
        var search = planSearch.Search(request);

        // Fail safe: every demanded category has an eligible supplier, so a cover exists.
        if (search.Plans.Count == 0)
        {
            throw new InvalidOperationException("The plan search found no plan although every demanded category has an eligible supplier.");
        }

        var tieBreak = tieBreaker.Pick(policy.Selection.SelectBest(search.Plans));
        var outcome = policy.Unfulfillable.Apply(tieBreak.Chosen, unfulfilled);
        var routed = outcome.Plan is { } plan ? RoutedLinesFor(order, plan, offers) : [];

        return ToDecision(outcome, routed, policy, tieBreak, search);
    }

    // One offer per supplier: every demanded category it may fulfill, and its mode for this order (FR-09).
    private static Dictionary<string, SupplierOffer> BuildOffers(
        Dictionary<Category, EligibilityResult> evaluations,
        IEnumerable<Category> demanded)
    {
        var offers = new Dictionary<string, SupplierOffer>(StringComparer.Ordinal);

        foreach (var category in demanded)
        {
            foreach (var eligible in evaluations[category].Suppliers)
            {
                if (!offers.TryGetValue(eligible.Supplier.Id, out var offer))
                {
                    offers[eligible.Supplier.Id] = offer = new SupplierOffer(eligible.Supplier, eligible.Mode);
                }
                else if (offer.Mode != eligible.Mode)
                {
                    // FR-09: the mode depends on the supplier and the order, never on the category.
                    throw new InvalidOperationException($"Supplier '{eligible.Supplier.Id}' has two fulfillment modes in one order.");
                }

                offer.Categories.Add(category);
            }
        }

        return offers;
    }

    // Request order is kept (FR-15). Unfulfilled lines have no entry in the plan, so they are skipped.
    private static List<RoutedLine> RoutedLinesFor(OrderToRoute order, RoutingPlan plan, Dictionary<string, SupplierOffer> offers)
    {
        var lines = new List<RoutedLine>();

        foreach (var line in order.Lines)
        {
            if (line.Category is not { } category || !plan.Assignments.TryGetValue(category, out var supplierId))
            {
                continue;
            }

            var offer = offers[supplierId];
            lines.Add(new RoutedLine(line.ProductCode, line.Quantity, category, offer.Supplier, offer.Mode));
        }

        return lines;
    }

    private static RoutingDecision ToDecision(
        RoutingOutcome outcome,
        IReadOnlyList<RoutedLine> routed,
        RoutingPolicy policy,
        TieBreakResult? tieBreak,
        PlanSearchResult? search)
        => new(outcome.Status, routed, outcome.Unfulfilled, policy.Assignment, tieBreak, search?.Mode, search?.FallbackReason);

    private sealed class SupplierOffer(Supplier supplier, FulfillmentMode mode)
    {
        public Supplier Supplier { get; } = supplier;

        public FulfillmentMode Mode { get; } = mode;

        public HashSet<Category> Categories { get; } = [];
    }
}
