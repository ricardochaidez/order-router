using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderRouter.Application.Routing.PlanSearch;
using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing;
using OrderRouter.Domain.Routing.PlanSearch;
using OrderRouter.Domain.Routing.Policies;
using OrderRouter.Domain.Routing.TieBreaking;

namespace OrderRouter.Application.Routing;

/// <summary>
/// Registers the routing services. It lives in the Application layer so the Api host only references
/// Application and Infrastructure (the layer rule, <c>LayerDependencyTests</c>) while the Domain types are wired here.
/// The host binds <see cref="RoutingOptions"/> and <see cref="PlanSearchOptions"/>, and Infrastructure supplies
/// <see cref="IRandomSource"/>, <see cref="ICapabilityResolver"/> and <see cref="IReferenceDataReader"/>.
/// </summary>
public static class RoutingServiceCollectionExtensions
{
    /// <summary>Registers the plan search, the domain routing service, the policy factory and the use case.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddOrderRouterRouting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IValidateOptions<RoutingOptions>, RoutingOptionsValidator>();
        services.AddSingleton<IPlanSearchService, PlanSearchService>();
        services.AddSingleton<IRoutingPolicyFactory, RoutingPolicyFactory>();
        services.AddSingleton<ITieBreaker, UniformRandomTieBreaker>();
        services.AddSingleton(provider => new SupplierEligibility(ZipRange.Parse(provider.GetRequiredService<IOptions<RoutingOptions>>().Value.NationwideZipRange)));
        services.AddSingleton<OrderRoutingService>();

        // Scoped, like the EF ports it depends on.
        services.AddScoped<IRouteOrderUseCase, RouteOrderUseCase>();

        return services;
    }
}
