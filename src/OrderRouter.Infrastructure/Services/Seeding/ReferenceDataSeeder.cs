using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Infrastructure.Services.Seeding;

/// <summary>
/// Applies the migrations and upserts the normalized reference data in one transaction (FR-04, ADR-0002).
/// It is deliberately <b>not</b> registered in dependency injection: only the integration-test fixture
/// creates it, so neither API startup nor <c>docker compose up</c> can migrate or seed (SC-22, SC-33).
/// </summary>
/// <param name="context">The context to migrate and write through.</param>
/// <param name="timeProvider">Source of the <c>seeded_at</c> timestamp (injected so tests can fix it).</param>
/// <param name="logger">Receives the single summary entry.</param>
public sealed class ReferenceDataSeeder(
    OrderRouterDataContext context,
    TimeProvider timeProvider,
    ILogger<ReferenceDataSeeder> logger)
{
    /// <summary>
    /// Migrates, reads and validates both files completely, then upserts everything in one transaction.
    /// A bad file fails before any data is written; a failure inside the transaction rolls everything back.
    /// Rows missing from the CSV are kept and only counted (OQ-24).
    /// </summary>
    /// <param name="files">The two normalized seed files.</param>
    /// <param name="ct">Cancels the seed.</param>
    /// <returns>The counts that were also logged.</returns>
    /// <exception cref="ReferenceDataSeedException">A file is missing or malformed, or a row is rejected.</exception>
    /// <example>
    /// <code>
    /// var summary = await seeder.SeedAsync(ReferenceDataSeedFiles.FromDirectory("Docs/Data/Seed"), ct);
    /// </code>
    /// </example>
    public async Task<ReferenceDataSeedSummary> SeedAsync(ReferenceDataSeedFiles files, CancellationToken ct)
    {
        // Migrations are schema, not data, so they may be applied even when the files turn out to be bad.
        await context.Database.MigrateAsync(ct);

        // Validate both files completely before the transaction opens, so a bad file never writes data (AC8).
        var suppliers = await NormalizedSeedFileReader.ReadSuppliersAsync(files.SuppliersPath, ct);
        var products = await NormalizedSeedFileReader.ReadProductsAsync(files.ProductsPath, ct);

        // One timestamp per run, written on insert and on update.
        var seededAt = timeProvider.GetUtcNow();
        var supplierIds = suppliers.Select(supplier => supplier.Id).ToArray();
        var productCodes = products.Select(product => product.Code).ToArray();

        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        await UpsertSuppliersAsync(suppliers, seededAt, ct);
        var (categoryCount, serviceAreaCount) = await ReplaceSupplierChildrenAsync(suppliers, supplierIds, ct);
        await UpsertProductsAsync(products, ct);

        // Counted after the upsert; rows missing from the CSV are reported, never deleted (OQ-24, AC4).
        var suppliersNotInCsv = await context.Suppliers.CountAsync(row => !supplierIds.Contains(row.SupplierId), ct);
        var productsNotInCsv = await context.Products.CountAsync(row => !productCodes.Contains(row.ProductCode), ct);

        await transaction.CommitAsync(ct);

        var summary = new ReferenceDataSeedSummary(
            suppliers.Count,
            products.Count,
            categoryCount,
            serviceAreaCount,
            suppliers.Count(supplier => supplier.Rating is null),
            suppliersNotInCsv,
            productsNotInCsv);

        logger.LogInformation(
            "Reference data seeded: suppliers {Suppliers}, products {Products}, categories {Categories}, service areas {ServiceAreas}, " +
            "unrated suppliers {UnratedSuppliers}, suppliers not in CSV {SuppliersNotInCsv}, products not in CSV {ProductsNotInCsv}",
            summary.SuppliersLoaded,
            summary.ProductsLoaded,
            summary.CategoriesLoaded,
            summary.ServiceAreasLoaded,
            summary.UnratedSuppliers,
            summary.SuppliersNotInCsv,
            summary.ProductsNotInCsv);

        return summary;
    }

    // Set-based statements: each table is one INSERT ... SELECT unnest(arrays). Interpolated values become
    // parameters, so nothing is concatenated into the SQL (no injection) and the statement count stays tiny.
    private async Task UpsertSuppliersAsync(IReadOnlyList<Supplier> suppliers, DateTimeOffset seededAt, CancellationToken ct)
    {
        var ids = suppliers.Select(supplier => supplier.Id).ToArray();
        var names = suppliers.Select(supplier => supplier.Name).ToArray();
        var ratings = suppliers.Select(supplier => supplier.Rating?.Value).ToArray();
        var mailOrder = suppliers.Select(supplier => supplier.CanMailOrder).ToArray();

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO suppliers (supplier_id, name, rating, can_mail_order, seeded_at)
            SELECT t.id, t.name, t.rating, t.can_mail_order, {seededAt}
            FROM unnest({ids}::text[], {names}::text[], {ratings}::numeric(4,2)[], {mailOrder}::boolean[])
                AS t(id, name, rating, can_mail_order)
            ON CONFLICT (supplier_id) DO UPDATE SET
                name = EXCLUDED.name,
                rating = EXCLUDED.rating,
                can_mail_order = EXCLUDED.can_mail_order,
                seeded_at = EXCLUDED.seeded_at
            """,
            ct);
    }

    // The categories and service areas of every CSV supplier are replaced as a unit, so a supplier that
    // lost a category or ZIP range does not keep it (AC3).
    private async Task<(int Categories, int ServiceAreas)> ReplaceSupplierChildrenAsync(
        IReadOnlyList<Supplier> suppliers, string[] supplierIds, CancellationToken ct)
    {
        await context.Database.ExecuteSqlAsync(
            $"DELETE FROM supplier_categories WHERE supplier_id = ANY({supplierIds})", ct);
        await context.Database.ExecuteSqlAsync(
            $"DELETE FROM supplier_service_areas WHERE supplier_id = ANY({supplierIds})", ct);

        var categories = suppliers
            .SelectMany(supplier => supplier.Categories.Select(category => (supplier.Id, Category: category.Value)))
            .ToArray();
        var categorySupplierIds = categories.Select(pair => pair.Id).ToArray();
        var categoryValues = categories.Select(pair => pair.Category).ToArray();

        var areas = suppliers
            .SelectMany(supplier => supplier.ServiceArea.Segments.Select(segment => (supplier.Id, segment.Start, segment.End)))
            .ToArray();
        var areaSupplierIds = areas.Select(area => area.Id).ToArray();
        var startZips = areas.Select(area => area.Start.Number).ToArray();
        var endZips = areas.Select(area => area.End.Number).ToArray();

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO supplier_categories (supplier_id, category)
            SELECT * FROM unnest({categorySupplierIds}::text[], {categoryValues}::text[])
            """,
            ct);
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO supplier_service_areas (supplier_id, start_zip, end_zip)
            SELECT * FROM unnest({areaSupplierIds}::text[], {startZips}::integer[], {endZips}::integer[])
            """,
            ct);

        return (categories.Length, areas.Length);
    }

    private async Task UpsertProductsAsync(IReadOnlyList<Product> products, CancellationToken ct)
    {
        var codes = products.Select(product => product.Code).ToArray();
        var names = products.Select(product => product.Name).ToArray();
        var categories = products.Select(product => product.Category.Value).ToArray();

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO products (product_code, name, category)
            SELECT * FROM unnest({codes}::text[], {names}::text[], {categories}::text[])
            ON CONFLICT (product_code) DO UPDATE SET
                name = EXCLUDED.name,
                category = EXCLUDED.category
            """,
            ct);
    }
}
