using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>OR-027 AC10: the caller-hash secret is required at startup and never committed.</summary>
public class CallerHashingHostTests(ReferenceDataFixture fixture)
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Startup_BlankSecret_FailsNamingTheSetting(string secret)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:OrderRouter", fixture.ConnectionString);
            builder.UseSetting("CallerHashing:Secret", secret);
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("CallerHashing:Secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationFiles_Always_DoNotCommitACallerHashSecret()
    {
        var root = FindRepositoryRoot();
        var committed = Directory.GetFiles(Path.Combine(root, "src", "OrderRouter.Api"), "appsettings*.json")
            .Append(Path.Combine(root, "src", "OrderRouter.Api", "experiments.json"));

        Assert.All(committed, file => Assert.DoesNotContain("CallerHashing", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnvExample_Always_HoldsOnlyAPlaceholderSecret()
    {
        var line = Assert.Single(File.ReadAllLines(Path.Combine(FindRepositoryRoot(), ".env.example")), l => l.StartsWith("CALLER_HASHING_SECRET=", StringComparison.Ordinal));

        Assert.Contains("dev-only", line, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OrderRouter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
