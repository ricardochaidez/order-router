using OrderRouter.Domain.Common;

namespace OrderRouter.Domain.Eligibility;

/// <summary>
/// The outcome of the eligibility rules for one category: either at least one eligible supplier, or a
/// reason there is none. Never <c>null</c> and never an exception, so "no supplier" is a value the caller
/// must handle (fail safe: nothing advances toward fulfillment without a supplier).
/// </summary>
public sealed class EligibilityResult
{
    private EligibilityResult(IReadOnlyList<EligibleSupplier> suppliers, UnfulfillableReason? reason)
    {
        Suppliers = suppliers;
        Reason = reason;
    }

    /// <summary>The eligible suppliers ordered by supplier ID (ordinal); empty when unfulfillable. Never <c>null</c>.</summary>
    public IReadOnlyList<EligibleSupplier> Suppliers { get; }

    /// <summary>Why nothing is eligible; <c>null</c> when <see cref="IsEligible"/>.</summary>
    public UnfulfillableReason? Reason { get; }

    /// <summary>True when at least one supplier is eligible.</summary>
    public bool IsEligible => Reason is null;

    /// <summary>Creates an eligible result. The list is copied and ordered by supplier ID (ordinal) for determinism.</summary>
    /// <param name="suppliers">At least one eligible supplier.</param>
    /// <exception cref="ArgumentNullException"><paramref name="suppliers"/> is null.</exception>
    /// <exception cref="DomainValidationException">The collection is empty: an empty set needs a reason.</exception>
    public static EligibilityResult Eligible(IEnumerable<EligibleSupplier> suppliers)
    {
        ArgumentNullException.ThrowIfNull(suppliers);

        var ordered = suppliers.OrderBy(entry => entry.Supplier.Id, StringComparer.Ordinal).ToArray();

        return ordered.Length > 0
            ? new EligibilityResult(Array.AsReadOnly(ordered), null)
            : throw new DomainValidationException("An eligible result needs at least one supplier; use Unfulfillable with a reason.");
    }

    /// <summary>Creates a result with no eligible supplier.</summary>
    /// <param name="reason">Why there is none (§9.4).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> is not a defined value.</exception>
    public static EligibilityResult Unfulfillable(UnfulfillableReason reason)
        => Enum.IsDefined(reason)
            ? new EligibilityResult([], reason)
            : throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown unfulfillable reason.");
}
