using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Domain.Eligibility;

/// <summary>
/// Stateless domain service: which suppliers may fulfill a category for an order, and how they would ship
/// (Technical Requirements §7.1, FR-06 to FR-09). The rules are fixed: no experiment, policy or
/// configuration flag can change them, and the only input besides the order is the nationwide range.
/// </summary>
/// <remarks>
/// Pure rules with no I/O. The caller loads suppliers (for example with <see cref="IReferenceDataReader"/>)
/// and passes them in, which keeps rules apart from persistence (CLAUDE.md, "Decouple rules from state").
/// </remarks>
/// <example>
/// <code>
/// var eligibility = new SupplierEligibility(ZipRange.Parse("00100-99999"));
/// var result = eligibility.Evaluate(new Category("wheelchair"), ZipCode.Parse("00001"), mailOrder: false, suppliers);
///
/// if (!result.IsEligible)
/// {
///     var code = result.Reason!.Value.Code(); // NO_SUPPLIER_FOR_CATEGORY_IN_AREA
/// }
/// </code>
/// </example>
public sealed class SupplierEligibility(ZipRange nationwideRange)
{
    /// <summary>Finds the eligible suppliers for a category and their fulfillment mode.</summary>
    /// <param name="category">The category to fulfill.</param>
    /// <param name="customerZip">The customer's ZIP.</param>
    /// <param name="mailOrder">Whether the order allows mail fulfillment.</param>
    /// <param name="suppliers">Candidate suppliers; may include ones that do not handle the category.</param>
    /// <returns>Eligible suppliers ordered by ID, or a reason there are none. Never <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="suppliers"/> is null.</exception>
    public EligibilityResult Evaluate(Category category, ZipCode customerZip, bool mailOrder, IEnumerable<Supplier> suppliers)
    {
        ArgumentNullException.ThrowIfNull(suppliers);

        var eligible = suppliers
            .Where(supplier => supplier.Handles(category) && IsEligible(supplier, customerZip, mailOrder))
            .Select(supplier => new EligibleSupplier(supplier, ModeFor(supplier, customerZip)))
            .ToArray();

        if (eligible.Length > 0)
        {
            return EligibilityResult.Eligible(eligible);
        }

        // §9.4: with mail off the gap is geographic; with mail on, no mail-capable supplier handles it at all.
        return EligibilityResult.Unfulfillable(
            mailOrder ? UnfulfillableReason.NoSupplierForCategory : UnfulfillableReason.NoSupplierForCategoryInArea);
    }

    // §7.1. SC-12: a local order excludes mail-only suppliers. SC-06/SC-07: a mail order needs can_mail_order and ignores the ZIP.
    private bool IsEligible(Supplier supplier, ZipCode customerZip, bool mailOrder)
        => mailOrder
            ? supplier.CanMailOrder
            : supplier.Serves(customerZip) && !supplier.IsMailOnly(nationwideRange);

    // FR-09: mail-only ships by mail; otherwise local when it serves the ZIP; otherwise mail.
    private FulfillmentMode ModeFor(Supplier supplier, ZipCode customerZip)
        => supplier.IsMailOnly(nationwideRange) || !supplier.Serves(customerZip)
            ? FulfillmentMode.Mail
            : FulfillmentMode.Local;
}
