using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Domain.Interfaces;

/// <summary>EXP-2 seam: the rating the routing uses for a supplier (ADR-0001).</summary>
public interface IEffectiveRatingPolicy
{
    /// <summary>Gets the effective rating, 0 to 10.</summary>
    /// <param name="supplier">The supplier.</param>
    decimal EffectiveRating(Supplier supplier);
}
