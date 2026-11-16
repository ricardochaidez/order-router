using Microsoft.Extensions.DependencyInjection;
using OrderRouter.Domain.Interfaces;

namespace OrderRouter.Infrastructure.Randomness;

/// <summary>Dependency-injection registration for the production random source.</summary>
public static class RandomnessServiceCollectionExtensions
{
    /// <summary>Registers <see cref="SystemRandomSource"/> as the only <see cref="IRandomSource"/> (NFR-08a).</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddOrderRouterRandomness(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRandomSource, SystemRandomSource>();

        return services;
    }
}
