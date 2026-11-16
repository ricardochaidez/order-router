using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Storage;
using OrderRouter.Infrastructure.Persistence;
using OrderRouter.Infrastructure.Persistence.RoutingOrders;

namespace OrderRouter.IntegrationTests.Persistence;

/// <summary>
/// Reads stored routing orders back from a migrated throwaway database (OR-028 AC1, AC4, AC5). Needs `docker compose up -d`.
/// </summary>
public class RoutingOrderReaderTests
{
    private const string owner = "hash-of-caller";

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
        string? callerHash = owner,
        IReadOnlyList<StoredOrderItem>? items = null,
        IReadOnlyList<RoutingWarning>? warnings = null)
        => new(
            id ?? Guid.CreateVersion7(),
            "ORD-1",
            callerHash,
            "02101",
            true,
            "partially_routed",
            false,
            """{"exp3_partial_routing":{"arm":"treatment","reason":"assigned"}}""",
            "cfg-1",
            "greedy",
            created,
            items ??
            [
                new StoredOrderItem(1, "P1", 1, "cane", "SUP-1", "Acme", "local", null, null),
                new StoredOrderItem(2, "P2", 3, "oxygen", "SUP-2", "Beta", "mail", null, null),
                new StoredOrderItem(3, "NOPE", 2, null, null, null, null, "UNKNOWN_PRODUCT", "Product code 'NOPE' was not found in the catalog."),
                new StoredOrderItem(4, "P1", 5, "cane", "SUP-1", "Acme", "local", null, null),
            ],
            warnings ?? [new RoutingWarning("W1", "First.", ["NOPE"], ["P1"]), new RoutingWarning("W2", "Second.", [], [])]);

    private static async Task SaveAsync(ThrowawayDatabase database, StoredRoutingOrder order)
    {
        await using var context = database.CreateContext();

        await new EfRoutingOrderStore(context).SaveAsync(order, Token);
    }

    private static async Task<StoredRoutingOrder?> FindAsync(ThrowawayDatabase database, Guid id, string callerHash, CapturingCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<OrderRouterDbContext>().UseNpgsql(database.ConnectionString);

        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        await using var context = new OrderRouterDbContext(options.Options);

        return await new EfRoutingOrderReader(context).FindAsync(id, callerHash, Token);
    }

    // AC1
    [Fact]
    public async Task FindAsync_StoredOrder_ReturnsEveryFieldItemsInRequestOrderAndWarningsInOrder()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var saved = Order();
        await SaveAsync(database, saved);

        var found = await FindAsync(database, saved.Id, owner);

        Assert.NotNull(found);

        // jsonb normalizes the whitespace of the arms JSON, so compare it as JSON and everything else as is.
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(saved.ExperimentArmsJson),
            System.Text.Json.Nodes.JsonNode.Parse(found.ExperimentArmsJson)));
        var withoutCollectionsAndArms = (StoredRoutingOrder order) => order with { Items = [], Warnings = [], ExperimentArmsJson = string.Empty };
        Assert.Equal(withoutCollectionsAndArms(saved), withoutCollectionsAndArms(found));
        Assert.Equal(saved.Items, found.Items);
        Assert.Equal(saved.Warnings.Select(w => (w.Code, w.Message)), found.Warnings.Select(w => (w.Code, w.Message)));
        Assert.Equal(["NOPE"], found.Warnings[0].UnfulfilledProductCodes);
        Assert.Equal(["P1"], found.Warnings[0].AffectedProductCodes);
        Assert.Empty(found.Warnings[1].UnfulfilledProductCodes);
    }

    // AC5
    [Fact]
    public async Task FindAsync_AnotherCallersHash_ReturnsNull()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var saved = Order();
        await SaveAsync(database, saved);

        Assert.Null(await FindAsync(database, saved.Id, "hash-of-someone-else"));
    }

    [Fact]
    public async Task FindAsync_UnknownId_ReturnsNull()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await SaveAsync(database, Order());

        Assert.Null(await FindAsync(database, Guid.CreateVersion7(), owner));
    }

    // Fail safe: an order stored without a caller can never be read back, whatever hash is offered.
    [Theory]
    [InlineData("hash-of-caller")]
    [InlineData("NULL")]
    [InlineData("null")]
    public async Task FindAsync_OrderStoredWithNullHash_NeverMatches(string offered)
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var saved = Order(callerHash: null);
        await SaveAsync(database, saved);

        Assert.Null(await FindAsync(database, saved.Id, offered));
    }

    [Fact]
    public async Task FindAsync_BlankHash_Throws()
    {
        await using var database = await CreateMigratedDatabaseAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => FindAsync(database, Guid.CreateVersion7(), " "));
    }

    // AC4: the order carries its own copy of the supplier name, so a re-seed cannot change it.
    [Fact]
    public async Task FindAsync_SupplierRenamedAfterRouting_StillShowsTheRoutingTimeName()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync(Token);
            await using var insert = new NpgsqlCommand("INSERT INTO suppliers (supplier_id, name, can_mail_order, seeded_at) VALUES ('SUP-1', 'Acme', false, now())", connection);
            await insert.ExecuteNonQueryAsync(Token);
        }

        var saved = Order();
        await SaveAsync(database, saved);

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync(Token);
            await using var rename = new NpgsqlCommand("UPDATE suppliers SET name = 'Renamed Inc' WHERE supplier_id = 'SUP-1'", connection);
            Assert.Equal(1, await rename.ExecuteNonQueryAsync(Token));
        }

        var found = await FindAsync(database, saved.Id, owner);

        Assert.NotNull(found);
        Assert.Equal("Acme", found.Items[0].SupplierName);
        Assert.DoesNotContain(found.Items, item => item.SupplierName == "Renamed Inc");
    }

    // Reading is safe: only SELECTs, and only on the order tables (reference data is never joined).
    [Fact]
    public async Task FindAsync_StoredOrder_IssuesOnlySelectsOnTheOrderTables()
    {
        await using var database = await CreateMigratedDatabaseAsync();
        var saved = Order();
        await SaveAsync(database, saved);
        var interceptor = new CapturingCommandInterceptor();

        await FindAsync(database, saved.Id, owner, interceptor);

        Assert.NotEmpty(interceptor.Commands);
        Assert.All(interceptor.Commands, sql => Assert.StartsWith("SELECT", sql.TrimStart(), StringComparison.OrdinalIgnoreCase));
        Assert.All(interceptor.Commands, sql => Assert.Contains("routing_order", sql, StringComparison.Ordinal));
        Assert.DoesNotContain(interceptor.Commands, sql => sql.Contains("suppliers", StringComparison.Ordinal) || sql.Contains("products", StringComparison.Ordinal));
        Assert.Contains(interceptor.Commands, sql => sql.Contains("caller_hash", StringComparison.Ordinal));
    }
}
