using OrderRouter.Domain.Common;
using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Domain.Routing.PlanSearch;

/// <summary>
/// An eligible supplier as the plan search sees it. The caller (OR-015) has already applied the
/// eligibility rules (OR-010) and the rating policy (OR-013), so the search never decides what an
/// unrated supplier is worth (the control arm gives it 0, which is why the range starts at 0).
/// </summary>
public sealed record SupplierCandidate
{
    /// <summary>Creates a candidate, reporting every invalid field in one exception.</summary>
    /// <param name="supplierId">Opaque supplier id; trimmed.</param>
    /// <param name="eligibleCategories">Categories the supplier may fulfill; at least one.</param>
    /// <param name="mode">Local delivery or mail for this order (FR-09).</param>
    /// <param name="effectiveRating">Rating after the policy filled in unrated suppliers, 0 to 10 (0 is an unrated supplier under the control arm).</param>
    /// <exception cref="DomainValidationException">One or more fields are invalid.</exception>
    public SupplierCandidate(
        string? supplierId,
        IEnumerable<Category>? eligibleCategories,
        FulfillmentMode mode,
        decimal effectiveRating)
    {
        var errors = new List<string>();
        var id = supplierId?.Trim();
        var categories = eligibleCategories is null ? [] : eligibleCategories.ToHashSet();

        if (string.IsNullOrEmpty(id))
        {
            errors.Add("supplier id must not be empty");
        }

        if (categories.Count == 0)
        {
            errors.Add("candidate must have at least one eligible category");
        }

        if (!Enum.IsDefined(mode))
        {
            errors.Add($"fulfillment mode is not defined (received {mode})");
        }

        if (effectiveRating is < 0m or > 10m)
        {
            errors.Add($"effective rating must be between 0 and 10 inclusive (received {effectiveRating})");
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException($"Invalid supplier candidate: {string.Join("; ", errors)}.");
        }

        SupplierId = id!;
        EligibleCategories = categories;
        Mode = mode;
        EffectiveRating = effectiveRating;
    }

    /// <summary>Opaque supplier id.</summary>
    public string SupplierId { get; }

    /// <summary>Categories the supplier may fulfill (a private copy; the caller cannot change it).</summary>
    public IReadOnlySet<Category> EligibleCategories { get; }

    /// <summary>Local delivery or mail. One mode per supplier for an order (FR-09).</summary>
    public FulfillmentMode Mode { get; }

    /// <summary>Rating used for quality and for choosing between suppliers.</summary>
    public decimal EffectiveRating { get; }
}
