using Microsoft.Extensions.DependencyInjection;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Infrastructure.Persistence;
using OrderRouter.Infrastructure.Persistence.ReferenceData;
using OrderRouter.Infrastructure.Persistence.Seeding;

namespace OrderRouter.UnitTests.Infrastructure.Persistence;

public class PersistenceRegistrationTests
{
    // An unreachable host: if anything tried to connect while resolving, these tests would fail or hang.
    private const string unreachable = "Host=unreachable.invalid;Database=unused;Timeout=1";

    [Fact]
    public void AddOrderRouterDatabase_Always_ResolvesBothPortsToTheEfImplementationsWithoutConnecting()
    {
        using var provider = new ServiceCollection().AddOrderRouterDatabase(unreachable).BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.IsType<EfCapabilityResolver>(scope.ServiceProvider.GetRequiredService<ICapabilityResolver>());
        Assert.IsType<EfReferenceDataReader>(scope.ServiceProvider.GetRequiredService<IReferenceDataReader>());
    }

    [Fact]
    public void AddOrderRouterDatabase_Always_RegistersBothPortsAsScoped()
    {
        var services = new ServiceCollection().AddOrderRouterDatabase(unreachable);

        Assert.Equal(ServiceLifetime.Scoped, services.Single(descriptor => descriptor.ServiceType == typeof(ICapabilityResolver)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, services.Single(descriptor => descriptor.ServiceType == typeof(IReferenceDataReader)).Lifetime);
    }

    [Fact]
    public void AddOrderRouterDatabase_Always_StillDoesNotRegisterTheSeeder()
    {
        using var provider = new ServiceCollection().AddOrderRouterDatabase(unreachable).BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<ReferenceDataSeeder>());
    }
}
