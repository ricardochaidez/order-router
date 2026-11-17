using Microsoft.EntityFrameworkCore;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Helpers;
using OrderRouter.Infrastructure.Providers;
using OrderRouter.Infrastructure;

namespace OrderRouter.Api.IntegrationTests.Persistence;

/// <summary>
/// AC1 and AC2 on the shared seeded database (containment checks, the database may hold older rows). The matching
/// rules are unit tested in <c>ProductCodeLookupTests</c>; these tests check the SQL and the lookup together.
/// </summary>
public class ProductCatalogTests(ReferenceDataFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private OrderRouterDataContext CreateContext(CapturingCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<OrderRouterDataContext>().UseNpgsql(fixture.ConnectionString);

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new OrderRouterDataContext(builder.Options);
    }

    // The same two steps the use case runs: keys from the domain, one query, matches back to the inputs.
    private static async Task<IReadOnlyDictionary<string, CapabilityResolution>> ResolveAsync(
        ProductCatalog catalog, IEnumerable<string?> codes)
    {
        var lookup = ProductCodeLookup.For(codes);

        return lookup.Resolve(await catalog.FindByKeysAsync(lookup.Keys, Token));
    }

    [Theory]
    [InlineData(" ox-port-024 ")]
    [InlineData("OX-PORT-024")]
    [InlineData("OX- PORT -024")]
    [InlineData("ox-port-024\t")]
    public async Task FindByKeysAsync_CaseAndWhitespaceVariants_ResolveToOxygen(string code)
    {
        await using var context = CreateContext();
        var catalog = new ProductCatalog(context);

        var results = await ResolveAsync(catalog, [code]);

        Assert.True(results[code].TryGetCategory(out var category));
        Assert.Equal(new Category("oxygen"), category);
    }

    [Fact]
    public async Task FindByKeysAsync_CodeThatIsAPrefixOfAnother_DoesNotMatchTheLongerCode()
    {
        await using var context = CreateContext();
        var catalog = new ProductCatalog(context);

        var results = await ResolveAsync(catalog, ["OX-PORT-02"]);

        Assert.False(results["OX-PORT-02"].IsKnown);
    }

    [Theory]
    [InlineData("XX-UNKNOWN-999")]
    [InlineData("OX-PORT-02_")]
    [InlineData("%")]
    [InlineData("OX-PORT-%")]
    public async Task FindByKeysAsync_UnknownOrWildcardCode_FindsNothing(string code)
    {
        await using var context = CreateContext();
        var catalog = new ProductCatalog(context);
        ProductCodeKey.TryCreate(code, out var key);

        var products = await catalog.FindByKeysAsync([key], Token);

        Assert.Empty(products);
    }

    [Fact]
    public async Task FindByKeysAsync_Batch_SendsOneQuery()
    {
        var interceptor = new CapturingCommandInterceptor();
        await using var context = CreateContext(interceptor);
        var catalog = new ProductCatalog(context);

        var results = await ResolveAsync(catalog, ["OX-PORT-024", " ox-port-024 ", "XX-UNKNOWN-999", "", null]);

        Assert.Equal(new Category("oxygen"), results["OX-PORT-024"].Category);
        Assert.Equal(new Category("oxygen"), results[" ox-port-024 "].Category);
        Assert.False(results["XX-UNKNOWN-999"].IsKnown);
        Assert.Single(interceptor.Commands);
    }

    [Fact]
    public async Task FindByKeysAsync_BatchWithRealProducts_ResolvesEverySampledSeedProduct()
    {
        var products = await global::OrderRouter.Infrastructure.Services.Seeding.NormalizedSeedFileReader
            .ReadProductsAsync(SeedFiles.Real.ProductsPath, Token);
        var sample = products.Take(50).ToList();
        await using var context = CreateContext();
        var catalog = new ProductCatalog(context);

        var results = await ResolveAsync(catalog, sample.Select(product => product.Code.ToLowerInvariant()));

        foreach (var product in sample)
        {
            Assert.Equal(product.Category, results[product.Code.ToLowerInvariant()].Category);
        }
    }

    [Fact]
    public async Task FindByKeysAsync_NoKeys_ReturnsEmptyAndSendsNoQuery()
    {
        var interceptor = new CapturingCommandInterceptor();
        await using var context = CreateContext(interceptor);
        var catalog = new ProductCatalog(context);

        var products = await catalog.FindByKeysAsync([], Token);

        Assert.Empty(products);
        Assert.Empty(interceptor.Commands);
    }

    [Fact]
    public async Task FindByKeysAsync_NullKeys_ThrowsArgumentNullException()
    {
        await using var context = CreateContext();
        var catalog = new ProductCatalog(context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => catalog.FindByKeysAsync(null!, Token));
    }

    [Fact]
    public async Task FindByKeysAsync_TwoStoredCodesDifferingOnlyByCase_ReturnsBothAndTheLookupThrows()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        await using (var migrating = database.CreateContext())
        {
            await migrating.Database.MigrateAsync(Token);
        }

        // Raw SQL on purpose: it bypasses the seeder's duplicate check, and this is a throwaway database.
        await database.ExecuteAsync(
            "INSERT INTO products (product_code, name, category) VALUES ('ABC-1', 'One', 'cpap'), ('abc-1', 'Two', 'cpap')",
            Token);
        await using var context = database.CreateContext();
        var catalog = new ProductCatalog(context);
        var lookup = ProductCodeLookup.For(["abc-1"]);

        var products = await catalog.FindByKeysAsync(lookup.Keys, Token);

        Assert.Equal(2, products.Count);
        var exception = Assert.Throws<InvalidOperationException>(() => lookup.Resolve(products));
        Assert.Contains("ABC-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("abc-1", exception.Message, StringComparison.Ordinal);
    }

    // OR-020 DR-02: which rule categories exist, in one query.
    [Fact]
    public async Task FindExistingCategoriesAsync_MixOfSeededAndUnknown_ReturnsOnlyTheSeededOnes()
    {
        var interceptor = new CapturingCommandInterceptor();
        await using var context = CreateContext(interceptor);
        var catalog = new ProductCatalog(context);

        var found = await catalog.FindExistingCategoriesAsync([new Category("Hospital Bed"), new Category("walker"), new Category("not a seeded category")], Token);

        Assert.Equal([new Category("hospital bed"), new Category("walker")], found.OrderBy(category => category.Value, StringComparer.Ordinal));
        Assert.Single(interceptor.Commands);
    }

    [Fact]
    public async Task FindExistingCategoriesAsync_NoCategories_SendsNoQuery()
    {
        var interceptor = new CapturingCommandInterceptor();
        await using var context = CreateContext(interceptor);

        Assert.Empty(await new ProductCatalog(context).FindExistingCategoriesAsync([], Token));
        Assert.Empty(interceptor.Commands);
    }
}
