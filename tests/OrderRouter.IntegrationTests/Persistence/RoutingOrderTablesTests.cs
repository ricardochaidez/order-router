using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace OrderRouter.IntegrationTests.Persistence;

/// <summary>
/// Checks the stored routing order tables on a real PostgreSQL throwaway database (OR-026, ADR-0007).
/// Needs `docker compose up -d`.
/// </summary>
public class RoutingOrderTablesTests
{
    private const string insertOrderSql =
        "INSERT INTO routing_orders (id, order_id, caller_hash, customer_zip, mail_order, status, feasible, experiment_arms, config_version, search_mode, created_at) " +
        "VALUES ('{0}', '{1}', 'hash', '02101', false, '{2}', true, '{{}}', 'v1', {3}, now());";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<ThrowawayDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await ThrowawayDatabase.CreateAsync(Token);

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(Token);

        return database;
    }

    private static string Order(Guid id, string orderId = "ORD-1", string status = "routed", string searchMode = "'exact'")
        => string.Format(System.Globalization.CultureInfo.InvariantCulture, insertOrderSql, id, orderId, status, searchMode);

    // AC1
    [Fact]
    public async Task MigrateAsync_OnEmptyDatabase_CreatesTheThreeTablesWithTheAdrColumns()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var orders = await ColumnsAsync(database, "routing_orders");
        var items = await ColumnsAsync(database, "routing_order_items");
        var warnings = await ColumnsAsync(database, "routing_order_warnings");

        Assert.Equal(
            [
                "caller_hash:text:YES", "config_version:text:NO", "created_at:timestamp with time zone:NO", "customer_zip:character:NO",
                "experiment_arms:jsonb:NO", "feasible:boolean:NO", "id:uuid:NO", "mail_order:boolean:NO", "order_id:text:NO",
                "search_mode:text:YES", "status:text:NO",
            ],
            orders);
        Assert.Equal(
            [
                "category:text:YES", "fulfillment_mode:text:YES", "line_number:integer:NO", "message:text:YES", "product_code:text:NO",
                "quantity:integer:NO", "reason_code:text:YES", "routing_order_id:uuid:NO", "supplier_id:text:YES", "supplier_name:text:YES",
            ],
            items);
        Assert.Equal(
            [
                "affected_product_codes:ARRAY:NO", "code:text:NO", "message:text:NO", "ordinal:integer:NO",
                "routing_order_id:uuid:NO", "unfulfilled_product_codes:ARRAY:NO",
            ],
            warnings);
    }

    [Fact]
    public async Task MigrateAsync_OnEmptyDatabase_CreatesTheOrderIdIndexAsNonUnique()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var indexes = await database.QueryAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'routing_orders' AND indexname = 'ix_routing_orders_order_id'", Token);

        Assert.Equal(["CREATE INDEX ix_routing_orders_order_id ON public.routing_orders USING btree (order_id)"], indexes);
    }

    [Fact]
    public async Task CustomerZip_IsAFiveCharacterColumn()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var length = await database.QueryAsync(
            "SELECT character_maximum_length FROM information_schema.columns WHERE table_name = 'routing_orders' AND column_name = 'customer_zip'", Token);

        Assert.Equal(["5"], length);
    }

    // AC2
    [Fact]
    public async Task InsertOrder_WithTheSameOrderIdTwice_SavesBothRows()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        await database.ExecuteAsync(Order(Guid.CreateVersion7()) + Order(Guid.CreateVersion7()), Token);
        var count = await database.QueryAsync("SELECT count(*) FROM routing_orders WHERE order_id = 'ORD-1'", Token);

        Assert.Equal(["2"], count);
    }

    // AC3
    [Fact]
    public async Task OrderTables_AfterMigration_HaveNoForeignKeyToSuppliers()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var foreignKeys = await database.QueryAsync(
            "SELECT conrelid::regclass || '->' || confrelid::regclass FROM pg_constraint " +
            "WHERE contype = 'f' AND conrelid::regclass::text LIKE 'routing\\_order%' ORDER BY 1", Token);

        Assert.Equal(["routing_order_items->routing_orders", "routing_order_warnings->routing_orders"], foreignKeys);
    }

    [Fact]
    public async Task InsertItem_ForASupplierThatDoesNotExist_IsAccepted()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var id = Guid.CreateVersion7();

        await database.ExecuteAsync(
            Order(id) +
            $"INSERT INTO routing_order_items (routing_order_id, line_number, product_code, quantity, category, supplier_id, supplier_name, fulfillment_mode) " +
            $"VALUES ('{id}', 1, 'P1', 1, 'Oxygen', 'SUP-GONE', 'Old Name', 'local')", Token);
        var count = await database.QueryAsync("SELECT count(*) FROM routing_order_items WHERE supplier_id = 'SUP-GONE'", Token);

        Assert.Equal(["1"], count);
    }

    [Fact]
    public async Task InsertOrder_WithNullCallerHashAndNullSearchMode_IsAccepted()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var id = Guid.CreateVersion7();

        await database.ExecuteAsync(Order(id, status: "not_routed", searchMode: "NULL").Replace("'hash'", "NULL", StringComparison.Ordinal), Token);
        var count = await database.QueryAsync("SELECT count(*) FROM routing_orders WHERE caller_hash IS NULL AND search_mode IS NULL", Token);

        Assert.Equal(["1"], count);
    }

    [Fact]
    public async Task InsertOrder_WithUnknownStatus_ViolatesCheckConstraint()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(Order(Guid.CreateVersion7(), status: "maybe"), Token));

        Assert.Equal("ck_routing_orders_status", exception.ConstraintName);
    }

    [Fact]
    public async Task InsertOrder_WithUnknownSearchMode_ViolatesCheckConstraint()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(Order(Guid.CreateVersion7(), searchMode: "'fast'"), Token));

        Assert.Equal("ck_routing_orders_search_mode", exception.ConstraintName);
    }

    [Theory]
    [InlineData("0", "local", "ck_routing_order_items_quantity")]
    [InlineData("1", "courier", "ck_routing_order_items_fulfillment_mode")]
    public async Task InsertItem_WithInvalidValue_ViolatesCheckConstraint(string quantity, string mode, string constraint)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var id = Guid.CreateVersion7();
        await database.ExecuteAsync(Order(id), Token);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
            $"INSERT INTO routing_order_items (routing_order_id, line_number, product_code, quantity, fulfillment_mode) VALUES ('{id}', 1, 'P1', {quantity}, '{mode}')", Token));

        Assert.Equal(constraint, exception.ConstraintName);
    }

    [Fact]
    public async Task InsertItem_WithLineNumberZero_ViolatesCheckConstraint()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var id = Guid.CreateVersion7();
        await database.ExecuteAsync(Order(id), Token);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
            $"INSERT INTO routing_order_items (routing_order_id, line_number, product_code, quantity) VALUES ('{id}', 0, 'P1', 1)", Token));

        Assert.Equal("ck_routing_order_items_line_number", exception.ConstraintName);
    }

    [Fact]
    public async Task InsertItem_ForAnOrderThatDoesNotExist_ViolatesForeignKey()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
            $"INSERT INTO routing_order_items (routing_order_id, line_number, product_code, quantity) VALUES ('{Guid.CreateVersion7()}', 1, 'P1', 1)", Token));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task DeleteOrder_Always_CascadesToItemsAndWarnings()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var id = Guid.CreateVersion7();
        await database.ExecuteAsync(
            Order(id) +
            $"INSERT INTO routing_order_items (routing_order_id, line_number, product_code, quantity) VALUES ('{id}', 1, 'P1', 1);" +
            $"INSERT INTO routing_order_warnings (routing_order_id, ordinal, code, message, unfulfilled_product_codes, affected_product_codes) " +
            $"VALUES ('{id}', 1, 'W', 'm', ARRAY['A'], ARRAY['B'])", Token);

        await database.ExecuteAsync($"DELETE FROM routing_orders WHERE id = '{id}'", Token);
        var items = await database.QueryAsync("SELECT count(*) FROM routing_order_items", Token);
        var warnings = await database.QueryAsync("SELECT count(*) FROM routing_order_warnings", Token);

        Assert.Equal(["0"], items);
        Assert.Equal(["0"], warnings);
    }

    [Fact]
    public async Task OrderRows_ThroughTheContext_RoundTripWithItemsAndWarnings()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var id = Guid.CreateVersion7();

        await using (var context = database.CreateContext())
        {
            context.RoutingOrders.Add(new Infrastructure.Persistence.RoutingOrders.RoutingOrderRow
            {
                Id = id,
                OrderId = "ORD-1",
                CustomerZip = "02101",
                Status = "partially_routed",
                ExperimentArms = "{}",
                ConfigVersion = "v1",
                CreatedAt = DateTimeOffset.UtcNow,
                Items = [new() { LineNumber = 1, ProductCode = "P1", Quantity = 2, ReasonCode = "UNKNOWN_PRODUCT", Message = "m" }],
                Warnings = [new() { Ordinal = 1, Code = "W", Message = "m", UnfulfilledProductCodes = ["A"], AffectedProductCodes = ["B", "C"] }],
            });
            await context.SaveChangesAsync(Token);
        }

        await using var read = database.CreateContext();
        var stored = await read.RoutingOrders.Include(order => order.Items).Include(order => order.Warnings).SingleAsync(Token);

        Assert.Equal(id, stored.Id);
        Assert.Null(stored.CallerHash);
        Assert.Single(stored.Items);
        Assert.Equal(["B", "C"], stored.Warnings[0].AffectedProductCodes);
    }

    private static Task<List<string>> ColumnsAsync(ThrowawayDatabase database, string table)
        => database.QueryAsync(
            $"SELECT column_name || ':' || data_type || ':' || is_nullable FROM information_schema.columns WHERE table_name = '{table}' ORDER BY column_name", Token);
}
