namespace OrderRouter.Infrastructure.Persistence.Seeding;

/// <summary>What one seed run loaded, returned to the caller and logged once (AC6, NFR-04).</summary>
/// <param name="SuppliersLoaded">Supplier rows read from the CSV and upserted.</param>
/// <param name="ProductsLoaded">Product rows read from the CSV and upserted.</param>
/// <param name="CategoriesLoaded">Supplier category rows written.</param>
/// <param name="ServiceAreasLoaded">Supplier service-area segments written.</param>
/// <param name="UnratedSuppliers">Loaded suppliers with no rating.</param>
/// <param name="SuppliersNotInCsv">Suppliers in the database that are not in the CSV. Kept, never deleted (OQ-24).</param>
/// <param name="ProductsNotInCsv">Products in the database that are not in the CSV. Kept, never deleted (OQ-24).</param>
public sealed record ReferenceDataSeedSummary(
    int SuppliersLoaded,
    int ProductsLoaded,
    int CategoriesLoaded,
    int ServiceAreasLoaded,
    int UnratedSuppliers,
    int SuppliersNotInCsv,
    int ProductsNotInCsv);
