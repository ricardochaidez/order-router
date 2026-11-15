using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace OrderRouter.IntegrationTests.Persistence;

/// <summary>
/// Applies the migrations to a real PostgreSQL throwaway database and inspects the catalog (AC1, AC2, AC3).
/// Needs `docker compose up -d`.
/// </summary>
public class SchemaMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<ThrowawayDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await ThrowawayDatabase.CreateAsync(Token);

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(Token);

        return database;
    }

    private static async Task<List<string>> SnapshotAsync(ThrowawayDatabase database)
    {
        var snapshot = new List<string>();

        snapshot.AddRange(await database.QueryAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY 1", Token));
        snapshot.AddRange(await database.QueryAsync(
            "SELECT table_name || '.' || column_name || ':' || data_type || ':' || coalesce(numeric_precision::text, '') || ',' || coalesce(numeric_scale::text, '') || ':' || is_nullable " +
            "FROM information_schema.columns WHERE table_schema = 'public' ORDER BY 1", Token));
        snapshot.AddRange(await database.QueryAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' ORDER BY 1", Token));
        snapshot.AddRange(await database.QueryAsync(
            "SELECT conrelid::regclass || '.' || conname || ':' || pg_get_constraintdef(oid) FROM pg_constraint WHERE connamespace = 'public'::regnamespace ORDER BY 1", Token));
        snapshot.AddRange(await database.QueryAsync(
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1", Token));

        return snapshot;
    }

    // AC1
    [Fact]
    public async Task MigrateAsync_OnEmptyDatabase_CreatesTheFourTablesAndBothIndexes()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var tables = await database.QueryAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY 1", Token);
        var indexes = await database.QueryAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND indexname LIKE 'ix\\_%' ORDER BY 1", Token);

        Assert.Equal(["__EFMigrationsHistory", "products", "supplier_categories", "supplier_service_areas", "suppliers"], tables);
        Assert.Collection(
            indexes,
            first => Assert.EndsWith("ON public.supplier_categories USING btree (category, supplier_id)", first, StringComparison.Ordinal),
            second => Assert.EndsWith("ON public.supplier_service_areas USING btree (start_zip, end_zip)", second, StringComparison.Ordinal));
    }

    // AC2
    [Fact]
    public async Task MigrateAsync_RunTwice_SucceedsWithNothingPendingAndIdenticalSchema()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var before = await SnapshotAsync(database);

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(Token);
        var pending = await context.Database.GetPendingMigrationsAsync(Token);
        var after = await SnapshotAsync(database);

        Assert.Empty(pending);
        Assert.Equal(before, after);
    }

    // AC3 / SC-09
    [Fact]
    public async Task SuppliersTable_AfterMigration_HasNoMailOnlyColumn()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var columns = await database.QueryAsync(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'suppliers' ORDER BY 1", Token);

        Assert.Equal(["can_mail_order", "name", "rating", "seeded_at", "supplier_id"], columns);
        Assert.DoesNotContain(columns, name => name.Contains("mail_only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Rating_WithTwoDecimalPlaces_IsStoredWithoutRounding()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        await database.ExecuteAsync(
            "INSERT INTO suppliers (supplier_id, name, rating, can_mail_order, seeded_at) VALUES ('s1', 'A', 6.75, true, now())", Token);
        var stored = await database.QueryAsync("SELECT rating::text FROM suppliers WHERE supplier_id = 's1'", Token);

        Assert.Equal(["6.75"], stored);
    }

    [Fact]
    public async Task Rating_IsNull_IsAccepted()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        await database.ExecuteAsync(
            "INSERT INTO suppliers (supplier_id, name, rating, can_mail_order, seeded_at) VALUES ('s1', 'A', NULL, false, now())", Token);
        var count = await database.QueryAsync("SELECT count(*) FROM suppliers WHERE rating IS NULL", Token);

        Assert.Equal(["1"], count);
    }

    [Theory]
    [InlineData("11")]
    [InlineData("0.99")]
    public async Task Rating_OutsideOneToTen_ViolatesCheckConstraint(string rating)
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
            $"INSERT INTO suppliers (supplier_id, name, rating, can_mail_order, seeded_at) VALUES ('s1', 'A', {rating}, true, now())", Token));

        Assert.Equal("ck_suppliers_rating", exception.ConstraintName);
    }

    [Fact]
    public async Task ServiceArea_StartAfterEnd_ViolatesCheckConstraint()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await database.ExecuteAsync(
            "INSERT INTO suppliers (supplier_id, name, rating, can_mail_order, seeded_at) VALUES ('s1', 'A', 5, true, now())", Token);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
            "INSERT INTO supplier_service_areas (supplier_id, start_zip, end_zip) VALUES ('s1', 200, 100)", Token));

        Assert.Equal("ck_supplier_service_areas_start_le_end", exception.ConstraintName);
    }

    [Fact]
    public async Task ServiceArea_ZipAboveFiveDigits_ViolatesBoundsConstraint()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await database.ExecuteAsync(
            "INSERT INTO suppliers (supplier_id, name, rating, can_mail_order, seeded_at) VALUES ('s1', 'A', 5, true, now())", Token);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
            "INSERT INTO supplier_service_areas (supplier_id, start_zip, end_zip) VALUES ('s1', 0, 100000)", Token));

        Assert.Equal("ck_supplier_service_areas_zip_bounds", exception.ConstraintName);
    }

    [Fact]
    public async Task ChildRow_WithUnknownSupplier_ViolatesForeignKey()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
            "INSERT INTO supplier_categories (supplier_id, category) VALUES ('missing', 'Oxygen')", Token));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task DeleteSupplier_Always_CascadesToCategoriesAndServiceAreas()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await database.ExecuteAsync(
            "INSERT INTO suppliers (supplier_id, name, rating, can_mail_order, seeded_at) VALUES ('s1', 'A', 5, true, now());" +
            "INSERT INTO supplier_categories (supplier_id, category) VALUES ('s1', 'Oxygen');" +
            "INSERT INTO supplier_service_areas (supplier_id, start_zip, end_zip) VALUES ('s1', 0, 99999)", Token);

        await database.ExecuteAsync("DELETE FROM suppliers WHERE supplier_id = 's1'", Token);
        var categories = await database.QueryAsync("SELECT count(*) FROM supplier_categories", Token);
        var areas = await database.QueryAsync("SELECT count(*) FROM supplier_service_areas", Token);

        Assert.Equal(["0"], categories);
        Assert.Equal(["0"], areas);
    }
}
