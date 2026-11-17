using Microsoft.EntityFrameworkCore;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Helpers;
using OrderRouter.Infrastructure.Providers;
using OrderRouter.Infrastructure;

namespace OrderRouter.Api.IntegrationTests.Persistence;

/// <summary>
/// AC4 at the read-path level: both implementations only send SELECT statements and track nothing.
/// The end-to-end check (a routing request leaves the tables unchanged) belongs to OR-015/OR-027.
/// </summary>
public class ReadOnlyReferenceDataTests(ReferenceDataFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadPath_ResolveAndReadSuppliers_SendsOnlySelectsAndTracksNothing()
    {
        var interceptor = new CapturingCommandInterceptor();
        var options = new DbContextOptionsBuilder<OrderRouterDataContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new OrderRouterDataContext(options);
        var catalog = new ProductCatalog(context);
        var reader = new ReferenceDataReader(context);

        await catalog.FindByKeysAsync(ProductCodeLookup.For(["OX-PORT-024", " wc-std-001 ", "nope"]).Keys, Token);
        await reader.GetSuppliersHandlingAsync([new Category("wheelchair"), new Category("oxygen")], Token);

        Assert.NotEmpty(interceptor.Commands);
        Assert.All(interceptor.Commands, sql => Assert.StartsWith("SELECT", sql.TrimStart(), StringComparison.OrdinalIgnoreCase));
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
