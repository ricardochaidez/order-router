using OrderRouter.Infrastructure.Persistence.Seeding;

namespace OrderRouter.IntegrationTests.Persistence;

/// <summary>
/// AC1 on the shared <c>orderrouter</c> database, which <see cref="ReferenceDataFixture"/> seeded from the
/// committed files. The database may also hold older rows (AC4), so these are containment checks.
/// </summary>
public class SeededReferenceDataTests(ReferenceDataFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SharedDatabase_AfterSeed_ContainsEverySupplierAndProductFromTheFiles()
    {
        var suppliers = await NormalizedSeedFileReader.ReadSuppliersAsync(SeedFiles.Real.SuppliersPath, Token);
        var products = await NormalizedSeedFileReader.ReadProductsAsync(SeedFiles.Real.ProductsPath, Token);

        var supplierIds = (await fixture.QueryAsync("SELECT supplier_id FROM suppliers", Token)).ToHashSet(StringComparer.Ordinal);
        var productCodes = (await fixture.QueryAsync("SELECT product_code FROM products", Token)).ToHashSet(StringComparer.Ordinal);

        Assert.True(supplierIds.IsSupersetOf(suppliers.Select(supplier => supplier.Id)));
        Assert.True(productCodes.IsSupersetOf(products.Select(product => product.Code)));
    }

    [Fact]
    public async Task SharedDatabase_AfterSeed_ContainsTheFourteenSentinels()
    {
        var sentinels = await fixture.QueryAsync("SELECT supplier_id FROM suppliers WHERE supplier_id ~ '^SUP-T0(0[1-9]|1[0-4])$'", Token);

        Assert.Equal(14, sentinels.Count);
    }

    [Fact]
    public async Task SharedDatabase_AfterSeed_SentinelChildRowsMatchTheFileExactly()
    {
        var suppliers = await NormalizedSeedFileReader.ReadSuppliersAsync(SeedFiles.Real.SuppliersPath, Token);
        var sentinel = suppliers.Single(supplier => supplier.Id == "SUP-T001");

        var categories = await fixture.QueryAsync("SELECT category FROM supplier_categories WHERE supplier_id = 'SUP-T001' ORDER BY 1", Token);
        var areas = await fixture.QueryAsync(
            "SELECT start_zip || '-' || end_zip FROM supplier_service_areas WHERE supplier_id = 'SUP-T001' ORDER BY 1", Token);

        Assert.Equal(sentinel.Categories.Select(category => category.Value).Order(StringComparer.Ordinal), categories);
        Assert.Equal(
            sentinel.ServiceArea.Segments.Select(segment => $"{segment.Start.Number}-{segment.End.Number}").Order(StringComparer.Ordinal),
            areas);
    }

    [Fact]
    public async Task SharedDatabase_AfterSeed_RecordsTheInitialMigration()
    {
        var migrations = await fixture.QueryAsync("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"", Token);

        Assert.Contains(migrations, id => id.EndsWith("_InitialReferenceDataSchema", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fixture_Summary_LoadedCountsEqualTheFileRowCounts()
    {
        var suppliers = await NormalizedSeedFileReader.ReadSuppliersAsync(SeedFiles.Real.SuppliersPath, Token);
        var products = await NormalizedSeedFileReader.ReadProductsAsync(SeedFiles.Real.ProductsPath, Token);

        Assert.Equal(suppliers.Count, fixture.Summary.SuppliersLoaded);
        Assert.Equal(products.Count, fixture.Summary.ProductsLoaded);
    }
}
