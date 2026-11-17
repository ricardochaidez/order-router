using Microsoft.Extensions.Logging;
using OrderRouter.Infrastructure.Services.Seeding;

namespace OrderRouter.Api.IntegrationTests.Persistence;

/// <summary>
/// Runs the seeder on throwaway databases (AC1-AC6, AC8; OR-026 AC4). Needs `docker compose up -d`.
/// </summary>
public class ReferenceDataSeederTests
{
    private static readonly DateTimeOffset firstRun = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (ReferenceDataSeeder Seeder, CapturingLogger<ReferenceDataSeeder> Logger, FixedTimeProvider Time) CreateSeeder(ThrowawayDatabase database)
    {
        var logger = new CapturingLogger<ReferenceDataSeeder>();
        var time = new FixedTimeProvider(firstRun);

        return (new ReferenceDataSeeder(database.CreateContext(), time, logger), logger, time);
    }

    private static Task<ReferenceDataSeedSummary> Seed(ThrowawayDatabase database, ReferenceDataSeedFiles files)
        => CreateSeeder(database).Seeder.SeedAsync(files, Token);

    // Everything except seeded_at, so two runs can be compared (AC2).
    private static async Task<List<string>> SnapshotAsync(ThrowawayDatabase database)
    {
        var rows = new List<string>();
        rows.AddRange(await database.QueryAsync(
            "SELECT 'S|' || supplier_id || '|' || name || '|' || coalesce(rating::text, 'NULL') || '|' || can_mail_order FROM suppliers ORDER BY 1", Token));
        rows.AddRange(await database.QueryAsync(
            "SELECT 'C|' || supplier_id || '|' || category FROM supplier_categories ORDER BY 1", Token));
        rows.AddRange(await database.QueryAsync(
            "SELECT 'A|' || supplier_id || '|' || start_zip || '|' || end_zip FROM supplier_service_areas ORDER BY 1", Token));
        rows.AddRange(await database.QueryAsync(
            "SELECT 'P|' || product_code || '|' || name || '|' || category FROM products ORDER BY 1", Token));

        return rows;
    }

    private static async Task<string> CountAsync(ThrowawayDatabase database, string table)
        => Assert.Single(await database.QueryAsync($"SELECT count(*) FROM {table}", Token));

    private static SeedFiles FileA() => SeedFiles.Create(
        ["S1,Acme,\"02130, 02114-02212\",\"cane, cpap\",7,y", "S2,Beta,00100-99999,oxygen,5.5,n"],
        ["P1,Cane,cane", "P2,Tank,oxygen"]);

    // AC1
    [Fact]
    public async Task SeedAsync_EmptyDatabaseAndRealFiles_AppliesMigrationsAndLoadsEveryRow()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        var suppliers = await NormalizedSeedFileReader.ReadSuppliersAsync(SeedFiles.Real.SuppliersPath, Token);
        var products = await NormalizedSeedFileReader.ReadProductsAsync(SeedFiles.Real.ProductsPath, Token);

        var summary = await Seed(database, SeedFiles.Real);

        Assert.Equal(suppliers.Count.ToString(), await CountAsync(database, "suppliers"));
        Assert.Equal(products.Count.ToString(), await CountAsync(database, "products"));
        Assert.Equal(suppliers.Sum(supplier => supplier.Categories.Count).ToString(), await CountAsync(database, "supplier_categories"));
        Assert.Equal(suppliers.Sum(supplier => supplier.ServiceArea.Segments.Count).ToString(), await CountAsync(database, "supplier_service_areas"));
        Assert.Equal(suppliers.Count, summary.SuppliersLoaded);
        Assert.Equal(["14"], await database.QueryAsync("SELECT count(*) FROM suppliers WHERE supplier_id LIKE 'SUP-T0__'", Token));
        Assert.Equal(["InitialCreate"], (await database.QueryAsync("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"", Token))
            .Select(id => id[(id.IndexOf('_', StringComparison.Ordinal) + 1)..]).ToList());
    }

    // AC2
    [Fact]
    public async Task SeedAsync_RunTwice_KeepsCountsAndBusinessColumnsAndNoDuplicates()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        var (seeder, _, time) = CreateSeeder(database);
        await seeder.SeedAsync(SeedFiles.Real, Token);
        var before = await SnapshotAsync(database);

        time.Now = firstRun.AddHours(1);
        var second = await seeder.SeedAsync(SeedFiles.Real, Token);
        var after = await SnapshotAsync(database);

        Assert.Equal(before, after);
        Assert.Equal(0, second.SuppliersNotInCsv);
        Assert.Equal(0, second.ProductsNotInCsv);
        Assert.Equal(["True"], await database.QueryAsync("SELECT count(*) = count(DISTINCT supplier_id) FROM suppliers", Token));
        Assert.Equal(["True"], await database.QueryAsync("SELECT count(*) = count(DISTINCT product_code) FROM products", Token));
        Assert.Equal(["0"], await database.QueryAsync(
            $"SELECT count(*) FROM suppliers WHERE seeded_at <> '{time.Now:O}'::timestamptz", Token));
    }

    // OR-026 AC4 / ADR-0007: stored orders are history; a re-seed never touches them, even when it renames or drops the supplier.
    [Fact]
    public async Task SeedAsync_WithStoredOrders_LeavesTheOrderTablesUnchanged()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        using var a = FileA();
        await Seed(database, a.Files);
        var id = Guid.CreateVersion7();
        await database.ExecuteAsync(
            "INSERT INTO routing_orders (id, order_id, caller_hash, customer_zip, mail_order, status, feasible, experiment_arms, config_version, search_mode, created_at) " +
            $"VALUES ('{id}', 'ORD-1', 'hash', '02130', false, 'routed', true, '{{}}', 'v1', 'exact', '2026-01-01T00:00:00Z');" +
            $"INSERT INTO routing_order_items (routing_order_id, line_number, product_code, quantity, category, supplier_id, supplier_name, fulfillment_mode) VALUES ('{id}', 1, 'P1', 1, 'cane', 'S1', 'Acme', 'local');" +
            $"INSERT INTO routing_order_warnings (routing_order_id, ordinal, code, message, unfulfilled_product_codes, affected_product_codes) VALUES ('{id}', 1, 'W', 'm', ARRAY['A'], ARRAY['B'])",
            Token);
        var before = await OrderSnapshotAsync(database);
        using var renamedAndDropped = SeedFiles.Create(["S1,Acme Renamed,10001,walker,,n"], ["P1,Cane,cane"]);

        await Seed(database, renamedAndDropped.Files);
        var after = await OrderSnapshotAsync(database);

        Assert.Equal(["Acme Renamed"], await database.QueryAsync("SELECT name FROM suppliers WHERE supplier_id = 'S1'", Token));
        Assert.Equal(3, before.Count);
        Assert.Equal(before, after);
        Assert.Contains(after, row => row.Contains("|Acme|", StringComparison.Ordinal));
    }

    private static async Task<List<string>> OrderSnapshotAsync(ThrowawayDatabase database)
    {
        var rows = new List<string>();
        rows.AddRange(await database.QueryAsync("SELECT 'O|' || id || '|' || order_id || '|' || coalesce(caller_hash, '') || '|' || status || '|' || created_at FROM routing_orders ORDER BY 1", Token));
        rows.AddRange(await database.QueryAsync("SELECT 'I|' || line_number || '|' || product_code || '|' || coalesce(supplier_id, '') || '|' || coalesce(supplier_name, '') || '|' || coalesce(fulfillment_mode, '') FROM routing_order_items ORDER BY 1", Token));
        rows.AddRange(await database.QueryAsync("SELECT 'W|' || ordinal || '|' || code || '|' || array_to_string(unfulfilled_product_codes, ',') FROM routing_order_warnings ORDER BY 1", Token));

        return rows;
    }

    // AC3
    [Fact]
    public async Task SeedAsync_ChangedSupplier_ReplacesItsFieldsAndChildRowsAndLeavesOthersAlone()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        using var a = FileA();
        await Seed(database, a.Files);
        var s2Before = (await SnapshotAsync(database)).Where(row => row.Contains("|S2|", StringComparison.Ordinal) || row.StartsWith("S|S2", StringComparison.Ordinal)).ToList();
        using var b = SeedFiles.Create(
            ["S1,Acme Two,10001,walker,,n", "S2,Beta,00100-99999,oxygen,5.5,n"],
            ["P1,Cane,cane", "P2,Tank,oxygen"]);

        await Seed(database, b.Files);

        Assert.Equal(["walker"], await database.QueryAsync("SELECT category FROM supplier_categories WHERE supplier_id = 'S1'", Token));
        Assert.Equal(["10001-10001"], await database.QueryAsync("SELECT start_zip || '-' || end_zip FROM supplier_service_areas WHERE supplier_id = 'S1'", Token));
        Assert.Equal(["Acme Two|NULL|false"], await database.QueryAsync(
            "SELECT name || '|' || coalesce(rating::text, 'NULL') || '|' || can_mail_order FROM suppliers WHERE supplier_id = 'S1'", Token));
        var s2After = (await SnapshotAsync(database)).Where(row => row.Contains("|S2|", StringComparison.Ordinal) || row.StartsWith("S|S2", StringComparison.Ordinal)).ToList();
        Assert.Equal(s2Before, s2After);
    }

    // AC4
    [Fact]
    public async Task SeedAsync_RowsMissingFromTheNewFile_AreKeptAndCounted()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        using var a = FileA();
        await Seed(database, a.Files);
        using var b = SeedFiles.Create(["S1,Acme,\"02130, 02114-02212\",\"cane, cpap\",7,y"], ["P1,Cane,cane"]);

        var summary = await Seed(database, b.Files);

        Assert.Equal(["S2"], await database.QueryAsync("SELECT supplier_id FROM suppliers WHERE supplier_id = 'S2'", Token));
        Assert.Equal(["oxygen"], await database.QueryAsync("SELECT category FROM supplier_categories WHERE supplier_id = 'S2'", Token));
        Assert.Equal(["100-99999"], await database.QueryAsync("SELECT start_zip || '-' || end_zip FROM supplier_service_areas WHERE supplier_id = 'S2'", Token));
        Assert.Equal(["P2"], await database.QueryAsync("SELECT product_code FROM products WHERE product_code = 'P2'", Token));
        Assert.Equal(1, summary.SuppliersNotInCsv);
        Assert.Equal(1, summary.ProductsNotInCsv);
    }

    // AC5
    [Fact]
    public async Task SeedAsync_MissingSuppliersFile_ThrowsNamingThePathAndWritesNothing()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        using var a = FileA();
        var missing = Path.Combine(Path.GetTempPath(), "order-router-tests", "no-such-folder", "suppliers.normalized.csv");

        var exception = await Assert.ThrowsAsync<ReferenceDataSeedException>(
            () => Seed(database, a.Files with { SuppliersPath = missing }));

        Assert.Contains(missing, exception.Message, StringComparison.Ordinal);
        Assert.Equal("0", await CountAsync(database, "suppliers"));
        Assert.Equal("0", await CountAsync(database, "products"));
    }

    [Fact]
    public async Task SeedAsync_MissingProductsFile_ThrowsAndWritesNoSuppliers()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        using var a = FileA();
        var missing = Path.Combine(Path.GetTempPath(), "order-router-tests", "no-such-folder", "products.normalized.csv");

        await Assert.ThrowsAsync<ReferenceDataSeedException>(() => Seed(database, a.Files with { ProductsPath = missing }));

        Assert.Equal("0", await CountAsync(database, "suppliers"));
    }

    // AC6
    [Fact]
    public async Task SeedAsync_Always_LogsExactlyOneInformationSummaryMatchingTheReturnedCounts()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        var (seeder, logger, _) = CreateSeeder(database);
        using var files = SeedFiles.Create(
            ["S1,Acme,\"02130, 02114-02212\",\"cane, cpap\",,y", "S2,Beta,00100-99999,oxygen,5.5,n"],
            ["P1,Cane,cane", "P2,Tank,oxygen", "P3,Mask,cpap"]);

        var summary = await seeder.SeedAsync(files.Files, Token);

        Assert.Equal(new ReferenceDataSeedSummary(2, 3, 3, 3, 1, 0, 0), summary);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("suppliers 2, products 3, categories 3, service areas 3, unrated suppliers 1, suppliers not in CSV 0, products not in CSV 0", entry.Message, StringComparison.Ordinal);
    }

    // AC8
    [Theory]
    [InlineData("S1,Acme,2130,cane,7,y", "ZIP")]
    [InlineData("S1,Acme,02130,cane,11,y", "Rating")]
    public async Task SeedAsync_InvalidSupplierRow_ThrowsWithFileAndRowAndLeavesTheDatabaseUnchanged(string badRow, string reason)
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        using var a = FileA();
        await Seed(database, a.Files);
        var before = await SnapshotAsync(database);
        using var bad = SeedFiles.Create(["S2,Beta,00100-99999,oxygen,5.5,n", badRow], ["P1,Cane,cane"]);

        var exception = await Assert.ThrowsAsync<ReferenceDataSeedException>(() => Seed(database, bad.Files));

        Assert.Equal("suppliers.normalized.csv", exception.FileName);
        Assert.Equal(3, exception.RowNumber);
        Assert.Contains(reason, exception.Reason, StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync(database));
    }

    [Fact]
    public async Task SeedAsync_ValidSuppliersButInvalidProducts_ChangesNoSupplierRows()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        using var a = FileA();
        await Seed(database, a.Files);
        var before = await SnapshotAsync(database);
        using var bad = SeedFiles.Create(["S1,Changed Name,10001,walker,9,n"], ["P1,Cane,cane", ",NoCode,cane"]);

        var exception = await Assert.ThrowsAsync<ReferenceDataSeedException>(() => Seed(database, bad.Files));

        Assert.Equal("products.normalized.csv", exception.FileName);
        Assert.Equal(3, exception.RowNumber);
        Assert.Equal(before, await SnapshotAsync(database));
    }
}
