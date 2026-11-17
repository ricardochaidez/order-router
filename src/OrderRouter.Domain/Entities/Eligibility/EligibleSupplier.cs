using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Domain.Entities.Eligibility;

/// <summary>A supplier that may fulfill a category, with the way it would ship.</summary>
/// <param name="Supplier">The eligible supplier.</param>
/// <param name="Mode">Local delivery or mail (FR-09); ranking key 4 uses it (OR-013).</param>
public sealed record EligibleSupplier(Supplier Supplier, FulfillmentMode Mode);
