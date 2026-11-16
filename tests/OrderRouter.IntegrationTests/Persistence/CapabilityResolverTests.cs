using Microsoft.EntityFrameworkCore;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Infrastructure.Persistence;
using OrderRouter.Infrastructure.Persistence.ReferenceData;

namespace OrderRouter.IntegrationTests.Persistence;

/// <summary>AC1 and AC2 on the shared seeded database (containment checks, the database may hold older rows).</summary>
public class CapabilityResolverTests(ReferenceDataFixture fixture)
{
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

    [Theory]
    [InlineData(" ox-port-024 ")]
    [InlineData("OX-PORT-024")]
    [InlineData("OX- PORT -024")]
    [InlineData("ox-port-024\t")]
    public async Task ResolveAsync_CaseAndWhitespaceVariants_ResolveToOxygen(string code)
    {
        await using var context = CreateContext();
        var resolver = new EfCapabilityResolver(context);

        var result = await resolver.ResolveAsync(code, Token);

        Assert.True(result.TryGetCategory(out var category));
        Assert.Equal(new Category("oxygen"), category);
    }

    [Fact]
    public async Task ResolveAsync_CodeThatIsAPrefixOfAnother_DoesNotMatchTheLongerCode()
    {
        await using var context = CreateContext();
        var resolver = new EfCapabilityResolver(context);

        var result = await resolver.ResolveAsync("OX-PORT-02", Token);

        Assert.False(result.IsKnown);
    }

    [Theory]
    [InlineData("XX-UNKNOWN-999")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("OX-PORT-02_")]
    [InlineData("%")]
    [InlineData("OX-PORT-%")]
    public async Task ResolveAsync_UnknownEmptyNullOrWildcardCode_ReturnsUnknownWithoutThrowing(string? code)
    {
        await using var context = CreateContext();
        var resolver = new EfCapabilityResolver(context);

        var result = await resolver.ResolveAsync(code, Token);

        Assert.False(result.IsKnown);
        Assert.Equal(CapabilityResolution.Unknown, result);
    }

    [Fact]
    public async Task ResolveAsync_Batch_MixedKnownAndUnknownGivesOneEntryPerInput()
    {
        var interceptor = new CapturingCommandInterceptor();
        await using var context = CreateContext(interceptor);
        var resolver = new EfCapabilityResolver(context);
        string?[] inputs = ["OX-PORT-024", " ox-port-024 ", "XX-UNKNOWN-999", "", "   ", null, "OX-PORT-024"];

        var results = await resolver.ResolveAsync(inputs, Token);

        Assert.Equal(5, results.Count);
        Assert.Equal(new Category("oxygen"), results["OX-PORT-024"].Category);
        Assert.Equal(new Category("oxygen"), results[" ox-port-024 "].Category);
        Assert.False(results["XX-UNKNOWN-999"].IsKnown);
        Assert.False(results[""].IsKnown);
        Assert.False(results["   "].IsKnown);
        Assert.Single(interceptor.Commands);
    }

    [Fact]
    public async Task ResolveAsync_BatchWithRealProducts_ResolvesEverySampledSeedProduct()
    {
        var products = await Infrastructure.Persistence.Seeding.NormalizedSeedFileReader
            .ReadProductsAsync(SeedFiles.Real.ProductsPath, Token);
        var sample = products.Take(50).ToList();
        await using var context = CreateContext();
        var resolver = new EfCapabilityResolver(context);

        var results = await resolver.ResolveAsync(sample.Select(product => product.Code.ToLowerInvariant()), Token);

        foreach (var product in sample)
        {
            Assert.Equal(product.Category, results[product.Code.ToLowerInvariant()].Category);
        }
    }

    [Fact]
    public async Task ResolveAsync_EmptyBatch_ReturnsEmptyAndSendsNoQuery()
    {
        var interceptor = new CapturingCommandInterceptor();
        await using var context = CreateContext(interceptor);
        var resolver = new EfCapabilityResolver(context);

        var results = await resolver.ResolveAsync(Array.Empty<string?>(), Token);

        Assert.Empty(results);
        Assert.Empty(interceptor.Commands);
    }

    [Fact]
    public async Task ResolveAsync_OnlyInvalidCodes_SendsNoQuery()
    {
        var interceptor = new CapturingCommandInterceptor();
        await using var context = CreateContext(interceptor);
        var resolver = new EfCapabilityResolver(context);

        var single = await resolver.ResolveAsync("   ", Token);
        var batch = await resolver.ResolveAsync(["", "  "], Token);

        Assert.False(single.IsKnown);
        Assert.All(batch.Values, value => Assert.False(value.IsKnown));
        Assert.Empty(interceptor.Commands);
    }

    [Fact]
    public async Task ResolveAsync_NullBatch_ThrowsArgumentNullException()
    {
        await using var context = CreateContext();
        var resolver = new EfCapabilityResolver(context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => resolver.ResolveAsync((IEnumerable<string?>)null!, Token));
    }

    [Fact]
    public async Task ResolveAsync_TwoStoredCodesDifferingOnlyByCase_ThrowsNamingBothCodes()
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
        var resolver = new EfCapabilityResolver(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync("abc-1", Token));

        Assert.Contains("ABC-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("abc-1", exception.Message, StringComparison.Ordinal);
    }
}
