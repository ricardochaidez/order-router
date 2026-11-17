using OrderRouter.Domain.Entities.Eligibility;
using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Domain.Models.PlanSearch;

/// <summary>A candidate reduced to a bitmask over the demanded categories.</summary>
/// <param name="SupplierId">Opaque supplier id.</param>
/// <param name="Mask">Bit i set when the supplier can fulfill demanded category i.</param>
/// <param name="Mode">Local or mail.</param>
/// <param name="Rating">Effective rating.</param>
public sealed record PreparedCandidate(string SupplierId, ulong Mask, FulfillmentMode Mode, decimal Rating);
