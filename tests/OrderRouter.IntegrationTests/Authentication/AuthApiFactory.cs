using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OrderRouter.Api.Authentication;

namespace OrderRouter.IntegrationTests.Authentication;

/// <summary>
/// Builds the real API host with the JWT test configuration and a protected probe endpoint. The probe exists only
/// in the test host: the real routes arrive in OR-009, which attaches the same policy to them.
/// </summary>
internal static class AuthApiFactory
{
    public const string ProbePath = "/test/probe";

    /// <summary>Authority and audience used when the environment is not Development.</summary>
    private const string testAuthority = "https://test-authority.example/";

    /// <summary>Creates the host. <paramref name="devApiKey"/> is the configured <c>Authentication:DevApiKey</c>, if any.</summary>
    public static WebApplicationFactory<Program> Create(
        string connectionString,
        string environment = "Development",
        string? devApiKey = null,
        ProbeCounter? probe = null,
        Action<ILoggingBuilder>? configureLogging = null,
        Action<IServiceCollection>? configureServices = null)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:OrderRouter", connectionString);
            builder.UseSetting("CallerHashing:Secret", TestCallerHashing.Secret);
            builder.UseSetting("Authentication:Jwt:Authority", testAuthority);
            builder.UseSetting("Authentication:Jwt:Audience", TestJwt.Audience);

            if (devApiKey is not null)
            {
                builder.UseSetting(DevApiKeyDefaults.ConfigurationKey, devApiKey);
            }

            if (configureLogging is not null)
            {
                builder.ConfigureLogging(configureLogging);
            }

            builder.ConfigureTestServices(services =>
            {
                // A static configuration replaces the OIDC metadata request: no network call in tests.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(TestJwt.Configuration()));

                services.AddSingleton<IStartupFilter>(new ProbeStartupFilter(probe ?? new ProbeCounter()));
                configureServices?.Invoke(services);
            });
        });

    /// <summary>Counts how many times the probe handler ran, so a test can prove it did not.</summary>
    public sealed class ProbeCounter
    {
        private int calls;

        public int Calls => calls;

        public void Increment() => Interlocked.Increment(ref calls);
    }

    // Adds the probe after Program.cs has built its pipeline, so requests still pass through the real
    // UseAuthentication/UseAuthorization middleware.
    private sealed class ProbeStartupFilter(ProbeCounter counter) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                next(app);
                app.UseEndpoints(endpoints => endpoints
                    .MapGet(ProbePath, (HttpContext context) =>
                    {
                        counter.Increment();

                        return Results.Ok(context.User.FindFirst("sub")?.Value);
                    })
                    .RequireAuthorization(AuthorizationPolicyNames.RouteCaller));
            };
    }
}
