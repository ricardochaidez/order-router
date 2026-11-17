using Microsoft.EntityFrameworkCore;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Infrastructure.Providers;

namespace OrderRouter.Api.IntegrationTests.Persistence;

/// <summary>
/// Saves stored routing orders into a migrated throwaway database (OR-027 AC1, AC2, AC3, AC7, AC8). Needs `docker compose up -d`.
/// </summary>
public class RoutingOrderStoreTests
{
    private static readonly DateTimeOffset created = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<ThrowawayDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await ThrowawayDatabase.CreateAsync(Token);

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(Token);

        return database;
    }

    private static StoredRoutingOrder Order(
        Guid? id = null,
        string? callerHash = "hash-of-caller",
        IReadOnlyList<StoredOrderItem>? items = null,
        IReadOnlyList<RoutingWarning>? warnings = null,
        string status = "routed")
        => new(
            id ?? Guid.CreateVersion7(),
            "ORD-1",
            callerHash,
            "02101",
            false,
            status,
            status == "routed",
            """{"exp1_consolidation_vs_quality":{"arm":"control","reason":"disabled"}}""",
            "cfg-1",
            "exact",
            created,
            items ?? [new StoredOrderItem(1, "P1", 1, "cane", "S1", "Acme", "local", null, null)],
            warnings ?? []);

    private static async Task SaveAsync(ThrowawayDatabase database, StoredRoutingOrder order)
    {
        await using var context = database.CreateContext();

        await new RoutingOrderStore(context).SaveAsync(order, Token);
    }

    // AC1, AC3
    [Fact]
    public async Task SaveAsync_RoutedOrder_StoresOneOrderRowAndOneItemRowPerLine()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var id = Guid.CreateVersion7();
        var items = new StoredOrderItem[]
        {
            new(1, "P1", 1, "cane", "SUP-1", "Acme", "local", null, null),
            new(2, "P2", 3, "oxygen", "SUP-2", "Beta", "mail", null, null),
        };

        await SaveAsync(database, Order(id, items: items));

        Assert.Equal(["1"], await database.QueryAsync("SELECT count(*) FROM routing_orders", Token));
        Assert.Equal(
            [$"{id}|ORD-1|hash-of-caller|02101|false|routed|true|cfg-1|exact"],
            await database.QueryAsync(
                "SELECT id || '|' || order_id || '|' || caller_hash || '|' || customer_zip || '|' || mail_order || '|' || status || '|' || feasible || '|' || config_version || '|' || search_mode FROM routing_orders", Token));
        Assert.Equal(["true"], await database.QueryAsync("SELECT (created_at = '2026-11-15T10:00:00Z'::timestamptz)::text FROM routing_orders", Token));
        Assert.Equal(
            ["1|P1|1|cane|SUP-1|Acme|local", "2|P2|3|oxygen|SUP-2|Beta|mail"],
            await database.QueryAsync(
                "SELECT line_number || '|' || product_code || '|' || quantity || '|' || category || '|' || supplier_id || '|' || supplier_name || '|' || fulfillment_mode FROM routing_order_items ORDER BY line_number", Token));
    }

    // AC3
    [Fact]
    public async Task SaveAsync_AnyOrder_StoresTheExperimentArms()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        await SaveAsync(database, Order());

        Assert.Equal(
            ["control"],
            await database.QueryAsync("SELECT experiment_arms -> 'exp1_consolidation_vs_quality' ->> 'arm' FROM routing_orders", Token));
    }

    // AC2
    [Fact]
    public async Task SaveAsync_UnknownProductLine_StoresReasonCodeAndMessage()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var items = new StoredOrderItem[] { new(1, "NOPE", 1, null, null, null, null, "UNKNOWN_PRODUCT", "Product code 'NOPE' was not found in the catalog.") };

        await SaveAsync(database, Order(items: items, status: "not_routed"));

        Assert.Equal(
            ["NOPE|UNKNOWN_PRODUCT|Product code 'NOPE' was not found in the catalog.|true"],
            await database.QueryAsync("SELECT product_code || '|' || reason_code || '|' || message || '|' || (supplier_id IS NULL) FROM routing_order_items", Token));
    }

    [Fact]
    public async Task SaveAsync_WithWarnings_StoresThemInOrder()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var warnings = new RoutingWarning[]
        {
            new("W1", "first", ["A"], ["B", "C"]),
            new("W2", "second", [], ["D"]),
        };

        await SaveAsync(database, Order(warnings: warnings));

        Assert.Equal(
            ["1|W1|first|A|B,C", "2|W2|second||D"],
            await database.QueryAsync(
                "SELECT ordinal || '|' || code || '|' || message || '|' || array_to_string(unfulfilled_product_codes, ',') || '|' || array_to_string(affected_product_codes, ',') FROM routing_order_warnings ORDER BY ordinal", Token));
    }

    // AC8: only the hash reaches the tables; a null hash is stored as NULL.
    [Fact]
    public async Task SaveAsync_CallerHash_IsStoredAndNullIsStoredAsNull()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        await SaveAsync(database, Order(callerHash: "abc123"));
        await SaveAsync(database, Order(callerHash: null));

        Assert.Equal(["abc123", "NULL"], await database.QueryAsync("SELECT coalesce(caller_hash, 'NULL') FROM routing_orders ORDER BY 1", Token));
    }

    // AC7 at store level: one bad row means nothing is stored (a single transaction).
    [Fact]
    public async Task SaveAsync_WhenALaterRowIsRejected_LeavesNoPartialRows()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var items = new StoredOrderItem[]
        {
            new(1, "P1", 1, "cane", "S1", "Acme", "local", null, null),
            new(2, "P2", 0, "cane", "S1", "Acme", "local", null, null),
        };

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => SaveAsync(database, Order(items: items)));

        Assert.Equal(["0"], await database.QueryAsync("SELECT count(*) FROM routing_orders", Token));
        Assert.Equal(["0"], await database.QueryAsync("SELECT count(*) FROM routing_order_items", Token));
    }

    // SC-29
    [Fact]
    public async Task SaveAsync_SameOrderIdTwice_StoresTwoOrders()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        await SaveAsync(database, Order());
        await SaveAsync(database, Order());

        Assert.Equal(["2"], await database.QueryAsync("SELECT count(*) FROM routing_orders WHERE order_id = 'ORD-1'", Token));
    }
}
