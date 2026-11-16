using OrderRouter.Domain.Common;
using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Infrastructure.Persistence.ReferenceData;

/// <summary>
/// Maps persistence rows to domain objects through the OR-002 constructors, which stay the only place
/// invariants are checked. A row they reject is never skipped or repaired (NFR-10): it throws with the supplier ID.
/// </summary>
internal static class SupplierRowMapper
{
    /// <summary>Builds a <see cref="Supplier"/> from a row loaded with its categories and service areas.</summary>
    /// <param name="row">The supplier row, with <see cref="SupplierRow.Categories"/> and <see cref="SupplierRow.ServiceAreas"/> loaded.</param>
    /// <exception cref="InvalidOperationException">The stored data violates a domain invariant; the inner exception says which.</exception>
    public static Supplier ToDomain(SupplierRow row)
    {
        try
        {
            var categories = row.Categories.Select(child => new Category(child.Category));
            var segments = row.ServiceAreas.Select(
                area => new ZipRange(ZipCode.FromNumber(area.StartZip), ZipCode.FromNumber(area.EndZip)));

            return new Supplier(row.SupplierId, row.Name, categories, new ServiceArea(segments), row.Rating, row.CanMailOrder);
        }
        catch (DomainValidationException exception)
        {
            throw new InvalidOperationException(
                $"Stored supplier \"{row.SupplierId}\" is invalid and cannot be used for routing: {exception.Message}",
                exception);
        }
    }
}
