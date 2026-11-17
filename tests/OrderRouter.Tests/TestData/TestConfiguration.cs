using Microsoft.Extensions.Configuration;

namespace OrderRouter.Tests.TestData;

/// <summary>Builds the in-memory configuration the registration tests hand to the Api extension methods.</summary>
internal static class TestConfiguration
{
    /// <summary>A configuration whose <c>ConnectionStrings:OrderRouter</c> is the given value (which may be blank or null).</summary>
    /// <param name="connectionString">The connection string to expose.</param>
    public static IConfiguration WithConnectionString(string? connectionString)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:OrderRouter"] = connectionString })
            .Build();
}
