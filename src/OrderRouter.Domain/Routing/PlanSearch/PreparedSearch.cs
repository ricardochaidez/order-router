using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Domain.Routing.PlanSearch;

/// <summary>A candidate reduced to a bitmask over the demanded categories.</summary>
/// <param name="SupplierId">Opaque supplier id.</param>
/// <param name="Mask">Bit i set when the supplier can fulfill demanded category i.</param>
/// <param name="Mode">Local or mail.</param>
/// <param name="Rating">Effective rating.</param>
public sealed record PreparedCandidate(string SupplierId, uint Mask, FulfillmentMode Mode, decimal Rating);

/// <summary>The request in the form the search runs on: bit-indexed categories and pruned candidates.</summary>
/// <param name="Categories">Demanded categories; the index is the bit number (ordinal order, so input order never matters).</param>
/// <param name="LineCounts">Line count per category, same index.</param>
/// <param name="Candidates">Surviving candidates, ordered by supplier id (ordinal).</param>
public sealed record PreparedSearch(
    IReadOnlyList<Category> Categories,
    IReadOnlyList<int> LineCounts,
    IReadOnlyList<PreparedCandidate> Candidates);
