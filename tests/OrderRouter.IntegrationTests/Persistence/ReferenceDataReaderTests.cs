using Microsoft.EntityFrameworkCore;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Infrastructure.Persistence;
using OrderRouter.Infrastructure.Persistence.ReferenceData;
using OrderRouter.Infrastructure.Persistence.Seeding;

namespace OrderRouter.IntegrationTests.Persistence;

/// <summary>AC3 on the shared seeded database (containment checks, the database may hold older rows).</summary>
public class ReferenceDataReaderTests(ReferenceDataFixture fixture)
{
    private static readonly Category wheelchair = new("wheelchair");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private OrderRouterDbContext CreateContext(CapturingCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<OrderRouterDbContext>().UseNpgsql(fixture.ConnectionString);

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new OrderRouterDbContext(builder.Options);
    }

    private static string[] Segments(Supplier supplier)
        => [.. supplier.ServiceArea.Segments.Select(segment => segment.ToString()).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task GetSuppliersHandlingAsync_Wheelchair_ReturnsEverySeedSupplierWithFullSets()
    {
        var seed = await NormalizedSeedFileReader.ReadSuppliersAsync(SeedFiles.Real.SuppliersPath, Token);
        var expected = seed.Where(supplier => supplier.Handles(wheelchair)).ToList();
        await using var context = CreateContext();
        var reader = new EfReferenceDataReader(context);

        var suppliers = await reader.GetSuppliersHandlingAsync([wheelchair], Token);

        Assert.NotEmpty(expected);
        Assert.All(suppliers, supplier => Assert.True(supplier.Handles(wheelchair)));
        var byId = suppliers.ToDictionary(supplier => supplier.Id, StringComparer.Ordinal);
        Assert.Contains("SUP-T001", byId.Keys);

        foreach (var want in expected)
        {
            var got = byId[want.Id];

            Assert.Equal(want.Name, got.Name);
            Assert.True(want.Categories.SetEquals(got.Categories));
            Assert.Equal(Segments(want), Segments(got));
            Assert.Equal(want.Rating, got.Rating);
            Assert.Equal(want.CanMailOrder, got.CanMailOrder);
        }
    }

    [Fact]
    public async Task GetSuppliersHandlingAsync_SupplierWithSeveralCategories_ReturnsAllOfThemNotOnlyTheRequestedOne()
    {
        var seed = await NormalizedSeedFileReader.ReadSuppliersAsync(SeedFiles.Real.SuppliersPath, Token);
        var multi = seed.First(supplier => supplier.Handles(wheelchair) && supplier.Categories.Count > 1);
        await using var context = CreateContext();
        var reader = new EfReferenceDataReader(context);

        var suppliers = await reader.GetSuppliersHandlingAsync([wheelchair], Token);

        Assert.True(multi.Categories.SetEquals(suppliers.Single(supplier => supplier.Id == multi.Id).Categories));
    }

    [Fact]
    public async Task GetSuppliersHandlingAsync_SeveralCategories_ReturnsTheUnionWithoutDuplicates()
    {
        var seed = await NormalizedSeedFileReader.ReadSuppliersAsync(SeedFiles.Real.SuppliersPath, Token);
        var both = seed.First(supplier => supplier.Categories.Count > 1);
        var requested = both.Categories.Take(2).ToList();
        var expectedIds = seed.Where(supplier => requested.Any(supplier.Handles)).Select(supplier => supplier.Id).ToHashSet(StringComparer.Ordinal);
        await using var context = CreateContext();
        var reader = new EfReferenceDataReader(context);

        var suppliers = await reader.GetSuppliersHandlingAsync(requested, Token);

        var ids = suppliers.Select(supplier => supplier.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.True(ids.ToHashSet(StringComparer.Ordinal).IsSupersetOf(expectedIds));
        Assert.Single(ids, both.Id);
    }

    [Fact]
    public async Task GetSuppliersHandlingAsync_Result_IsOrderedBySupplierIdOrdinal()
    {
        await using var context = CreateContext();
        var reader = new EfReferenceDataReader(context);

        var suppliers = await reader.GetSuppliersHandlingAsync([wheelchair, new Category("cpap")], Token);

        var ids = suppliers.Select(supplier => supplier.Id).ToList();
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
    }

    [Fact]
    public async Task GetSuppliersHandlingAsync_UnknownCategory_ReturnsEmptyList()
    {
        await using var context = CreateContext();
        var reader = new EfReferenceDataReader(context);

        var suppliers = await reader.GetSuppliersHandlingAsync([new Category("no-such-category")], Token);

        Assert.Empty(suppliers);
    }

    [Fact]
    public async Task GetSuppliersHandlingAsync_EmptyOrDefaultCategories_ReturnsEmptyListAndSendsNoQuery()
    {
        var interceptor = new CapturingCommandInterceptor();
        await using var context = CreateContext(interceptor);
        var reader = new EfReferenceDataReader(context);

        var none = await reader.GetSuppliersHandlingAsync([], Token);
        var defaults = await reader.GetSuppliersHandlingAsync([default], Token);

        Assert.Empty(none);
        Assert.Empty(defaults);
        Assert.Empty(interceptor.Commands);
    }

    [Fact]
    public async Task GetSuppliersHandlingAsync_NullCategories_ThrowsArgumentNullException()
    {
        await using var context = CreateContext();
        var reader = new EfReferenceDataReader(context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => reader.GetSuppliersHandlingAsync(null!, Token));
    }

    [Fact]
    public async Task GetSuppliersHandlingAsync_StoredSupplierWithoutServiceAreas_ThrowsNamingTheSupplier()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        await using (var migrating = database.CreateContext())
        {
            await migrating.Database.MigrateAsync(Token);
        }

        // Raw SQL on purpose: a throwaway database, and the seeder would refuse this row.
        await database.ExecuteAsync(
            "INSERT INTO suppliers (supplier_id, name, can_mail_order, seeded_at) VALUES ('BAD-1', 'Broken', true, now());" +
            "INSERT INTO supplier_categories (supplier_id, category) VALUES ('BAD-1', 'cpap')",
            Token);
        await using var context = database.CreateContext();
        var reader = new EfReferenceDataReader(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.GetSuppliersHandlingAsync([new Category("cpap")], Token));

        Assert.Contains("BAD-1", exception.Message, StringComparison.Ordinal);
    }
}
