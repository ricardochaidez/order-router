using OrderRouter.Domain.ReferenceData;
namespace OrderRouter.Domain.Interfaces;

/// <summary>
/// Port: suppliers by category (FR-06). Each supplier is a whole aggregate with all of its categories and
/// service area, so OR-010 can apply ZIP and mail rules in the domain. Read-only (AC4).
/// </summary>
/// <example>
/// <code>
/// var suppliers = await reader.GetSuppliersHandlingAsync([new Category("wheelchair")], cancellationToken);
/// </code>
/// </example>
public interface IReferenceDataReader
{
    /// <summary>Gets the suppliers that handle <b>any</b> of the categories.</summary>
    /// <param name="categories">The requested categories; empty returns an empty list.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Suppliers without duplicates, ordered by supplier ID (ordinal). Never <c>null</c>.</returns>
    /// <exception cref="InvalidOperationException">A stored supplier violates a domain invariant (never skipped or repaired, NFR-10).</exception>
    Task<IReadOnlyList<Supplier>> GetSuppliersHandlingAsync(
        IEnumerable<Category> categories,
        CancellationToken cancellationToken);
}
