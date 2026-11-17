using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderRouter.Api.IntegrationTests.Persistence;
using OrderRouter.Infrastructure;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

public class StartupTests
{
    [Fact]
    public async Task Startup_EmptyUnmigratedDatabase_SucceedsReadsNoTablesAndCreatesNoTables()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(TestContext.Current.CancellationToken);
        var interceptor = new CapturingCommandInterceptor();
        await using var factory = ApiFactory.Create(
            database.ConnectionString,
            services => services.AddDbContext<OrderRouterDataContext>(options => options.AddInterceptors(interceptor), optionsLifetime: ServiceLifetime.Singleton));

        // Creating the client starts the host, which runs the startup check (AC6).
        using var client = factory.CreateClient();


        // EF's CanConnectAsync sends a bare `SELECT 1` to prove the server answers; nothing may read or change a table.
        Assert.All(interceptor.Commands, command => Assert.Equal("SELECT 1", command.Trim().TrimEnd(';')));
        var tables = await database.QueryAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'",
            TestContext.Current.CancellationToken);
        Assert.Empty(tables);
    }

    [Fact]
    public async Task Startup_DatabaseUnreachable_FailsFastNamingTheCheck()
    {
        await using var factory = ApiFactory.Create("Host=unreachable.invalid;Database=unused;Username=u;Password=super-secret;Timeout=1");

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("Dependency check failed at startup: postgresql is Unhealthy.", Flatten(exception), StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", Flatten(exception), StringComparison.Ordinal);
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }
}
