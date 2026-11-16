using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Domain.Routing.Policies;

/// <summary>Control arm of EXP-2: an unrated supplier counts as 0 but stays eligible (AB-03).</summary>
public sealed class UnratedLastPolicy : IEffectiveRatingPolicy
{
    private UnratedLastPolicy()
    {
    }

    /// <summary>Shared instance; the policy has no state.</summary>
    public static UnratedLastPolicy Instance { get; } = new();

    /// <inheritdoc />
    public decimal EffectiveRating(Supplier supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        return supplier.Rating?.Value ?? 0m;
    }
}
