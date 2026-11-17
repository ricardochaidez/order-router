using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Domain.Models.PlanSearch;

/// <summary>The request in the form the search runs on: bit-indexed categories and pruned candidates.</summary>
/// <param name="Categories">Demanded categories; the index is the bit number (ordinal order, so input order never matters).</param>
/// <param name="LineCounts">Line count per category, same index.</param>
/// <param name="Candidates">Surviving candidates, ordered by supplier id (ordinal).</param>
public sealed record PreparedSearch(
    IReadOnlyList<Category> Categories,
    IReadOnlyList<int> LineCounts,
    IReadOnlyList<PreparedCandidate> Candidates);
