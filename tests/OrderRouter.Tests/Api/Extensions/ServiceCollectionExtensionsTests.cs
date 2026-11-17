using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OrderRouter.Api.Extensions;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Infrastructure.Extensions;
using OrderRouter.Infrastructure.Providers;
using OrderRouter.Infrastructure.Services.Seeding;
using OrderRouter.Tests.TestData;

namespace OrderRouter.Tests.Api.Extensions;

public class ServiceCollectionExtensionsTests
{
    // An unreachable host: if anything tried to connect while resolving, these tests would fail or hang.
    private const string unreachable = "Host=unreachable.invalid;Database=unused;Timeout=1";

    [Fact]
    public void ConfigureDbContext_Always_ResolvesBothPortsToTheEfImplementationsWithoutConnecting()
    {
        using var provider = new ServiceCollection().ConfigureDbContext(TestConfiguration.WithConnectionString(unreachable)).BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.IsType<ProductCatalog>(scope.ServiceProvider.GetRequiredService<IProductCatalog>());
        Assert.IsType<ReferenceDataReader>(scope.ServiceProvider.GetRequiredService<IReferenceDataReader>());
    }

    [Fact]
    public void ConfigureDbContext_Always_RegistersBothPortsAsScoped()
    {
        var services = new ServiceCollection().ConfigureDbContext(TestConfiguration.WithConnectionString(unreachable));

        Assert.Equal(ServiceLifetime.Scoped, services.Single(descriptor => descriptor.ServiceType == typeof(IProductCatalog)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, services.Single(descriptor => descriptor.ServiceType == typeof(IReferenceDataReader)).Lifetime);
    }

    [Fact]
    public void ConfigureDbContext_Always_StillDoesNotRegisterTheSeeder()
    {
        using var provider = new ServiceCollection().ConfigureDbContext(TestConfiguration.WithConnectionString(unreachable)).BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<ReferenceDataSeeder>());
    }

    [Fact]
    public void ConfigureHealthChecks_Always_RegistersPostgresqlCheckWithTheDependencyTag()
    {
        var services = new ServiceCollection();
        services.ConfigureDbContext(TestConfiguration.WithConnectionString(unreachable));
        services.ConfigureHealthChecks();
        using var provider = services.BuildServiceProvider();

        var registration = Assert.Single(provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);

        Assert.Equal("postgresql", registration.Name);
        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
        Assert.Contains(HealthTags.Dependency, registration.Tags);
    }

    [Fact]
    public async Task ConfigureHealthChecks_ServerUnreachable_ReportsUnhealthyWithoutThrowing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureDbContext(TestConfiguration.WithConnectionString(unreachable));
        services.ConfigureHealthChecks();
        await using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
    }

    [Fact]
    public void ConfigureHealthChecks_NullServices_Throws()
        => Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.ConfigureHealthChecks(null!));
}
