using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderRouter.Application.Routing;

namespace OrderRouter.Infrastructure.CallerHashing;

/// <summary>Dependency-injection registration for the caller hasher.</summary>
public static class CallerHashingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICallerHasher"/> and validates <c>CallerHashing:Secret</c> at startup, so a host without
    /// the secret does not start (OR-027).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration holding the <c>CallerHashing</c> section.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddOrderRouterCallerHashing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CallerHashingOptions>()
            .Bind(configuration.GetSection(CallerHashingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CallerHashingOptions>, CallerHashingOptionsValidator>();
        services.AddSingleton<ICallerHasher, HmacCallerHasher>();

        return services;
    }
}
