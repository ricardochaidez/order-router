using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Models.PlanSearch;

namespace OrderRouter.Domain.Helpers;

/// <summary>
/// Turns a request into bitmasks and drops look-alike suppliers that can never win (ADR-0003).
/// Lines of one category are already one <see cref="CategoryDemand"/>, so they get one bit and are
/// fulfilled together (FR-10); a quantity never splits a line.
/// </summary>
public static class CandidatePreparer
{
    /// <summary>Assigns bits, masks candidates and prunes dominated ones.</summary>
    /// <param name="request">The validated request.</param>
    /// <returns>The prepared search.</returns>
    public static PreparedSearch Prepare(PlanSearchRequest request)
    {
        var demand = request.Demand.OrderBy(d => d.Category.Value, StringComparer.Ordinal).ToList();
        var bits = new Dictionary<Category, ulong>(demand.Count);

        for (var index = 0; index < demand.Count; index++)
        {
            bits[demand[index].Category] = 1UL << index;
        }

        var masked = new List<PreparedCandidate>();

        foreach (var candidate in request.Candidates)
        {
            var mask = 0UL;

            foreach (var category in candidate.EligibleCategories)
            {
                if (bits.TryGetValue(category, out var bit))
                {
                    mask |= bit;
                }
            }

            // A supplier that covers nothing demanded can never be part of a plan.
            if (mask != 0)
            {
                masked.Add(new PreparedCandidate(candidate.SupplierId, mask, candidate.Mode, candidate.EffectiveRating));
            }
        }

        // Same mask and mode: only the best rating can win. Equal-rating twins all stay so the
        // random tie-break has something to choose from (FR-13, AC5). A different mode is not a look-alike (AC6).
        var survivors = masked
            .GroupBy(c => (c.Mask, c.Mode))
            .SelectMany(group =>
            {
                var best = group.Max(c => c.Rating);

                return group.Where(c => c.Rating == best);
            })
            .OrderBy(c => c.SupplierId, StringComparer.Ordinal)
            .ToList();

        return new PreparedSearch(
            demand.Select(d => d.Category).ToList().AsReadOnly(),
            demand.Select(d => d.LineCount).ToList().AsReadOnly(),
            survivors.AsReadOnly());
    }
}
