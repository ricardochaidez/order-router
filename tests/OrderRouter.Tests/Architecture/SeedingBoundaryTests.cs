using Microsoft.Extensions.DependencyInjection;
using OrderRouter.Api.Extensions;
using OrderRouter.Infrastructure.Services.Seeding;
using OrderRouter.Tests.DataNormalizer;
using OrderRouter.Tests.TestData;

namespace OrderRouter.Tests.Architecture;

/// <summary>
/// Guards AC7 / SC-22 / SC-33: only the integration-test fixture may migrate or seed. The API must never do it,
/// and the seeder must not be resolvable from the API's dependency injection.
/// </summary>
public class SeedingBoundaryTests
{
    private static readonly string[] forbiddenInApi =
        ["ReferenceDataSeeder", "Migrate(", "MigrateAsync(", "EnsureCreated"];

    [Fact]
    public void ApiSources_Always_NeverMigrateOrSeed()
    {
        var apiDirectory = Path.Combine(TestRepository.Root, "src", "OrderRouter.Api");
        var sources = Directory.EnumerateFiles(apiDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(sources);
        var offenders = sources
            .Where(path => forbiddenInApi.Any(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ConfigureDbContext_Always_DoesNotRegisterTheSeeder()
    {
        using var provider = new ServiceCollection()
            .ConfigureDbContext(TestConfiguration.WithConnectionString("Host=localhost;Database=unused"))
            .BuildServiceProvider();

        Assert.Null(provider.GetService<ReferenceDataSeeder>());
    }
}
