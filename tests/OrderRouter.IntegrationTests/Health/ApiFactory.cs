using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace OrderRouter.IntegrationTests.Health;

/// <summary>Builds the real API host in Development against a chosen PostgreSQL database.</summary>
internal static class ApiFactory
{
    /// <summary>
    /// Creates a factory whose <c>ConnectionStrings:OrderRouter</c> points at <paramref name="connectionString"/>.
    /// Nothing is started until <c>CreateClient()</c>, so the startup check runs then.
    /// </summary>
    public static WebApplicationFactory<Program> Create(string connectionString, Action<IServiceCollection>? configureServices = null)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:OrderRouter", connectionString);
            builder.UseSetting("CallerHashing:Secret", TestCallerHashing.Secret);

            if (configureServices is not null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        });
}
