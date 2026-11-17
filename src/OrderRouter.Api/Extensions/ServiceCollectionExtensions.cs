using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Sections;
using OrderRouter.Domain.Services.PlanSearch;
using OrderRouter.Domain.Services.Policies;
using OrderRouter.Domain.Services;
using OrderRouter.Infrastructure.Extensions;
using OrderRouter.Infrastructure.HostedServices;
using OrderRouter.Infrastructure.Interfaces;
using OrderRouter.Infrastructure.Providers;
using OrderRouter.Infrastructure.Services;
using OrderRouter.Infrastructure;

namespace OrderRouter.Api.Extensions;

/// <summary>
/// The composition root's registrations, grouped by concern. The Api is the only project that knows every layer, so
/// it is the only place that binds a Domain interface to its Infrastructure implementation.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Name of the database health check.</summary>
    public const string DatabaseHealthCheckName = "postgresql";

    private static readonly TimeSpan healthCheckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Registers the EF Core context, its testable interface and the data-access providers. The API never migrates
    /// or seeds; that is an explicit, separate step (SC-22, SC-33).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Supplies <c>ConnectionStrings:OrderRouter</c>. There is deliberately no default, so a mis-configured deployment
    /// fails at startup instead of silently using a dev password.
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The connection string is not configured.</exception>
    public static IServiceCollection ConfigureDbContext(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("OrderRouter");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The connection string 'ConnectionStrings:OrderRouter' is not configured.");
        }

        services.AddDbContext<OrderRouterDataContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IOrderRouterDataContext>(provider => provider.GetRequiredService<OrderRouterDataContext>());

        // Scoped, to match the DbContext lifetime they depend on.
        services.AddScoped<IProductCatalog, ProductCatalog>();
        services.AddScoped<IReferenceDataReader, ReferenceDataReader>();

        // The only writer the API has: it inserts into the stored-order tables, never into reference data (ADR-0007).
        services.AddScoped<IRoutingOrderStore, RoutingOrderStore>();

        // The read side of the same tables (OR-028): reads only, filtered by the caller's hash.
        services.AddScoped<IRoutingOrderReader, RoutingOrderReader>();

        return services;
    }

    /// <summary>
    /// Registers options, the domain services (plan search, policies, routing, the two order services) and the
    /// Infrastructure adapters they depend on.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration the option sections are bound from.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection ConfigureServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Routing settings (FR-09): a bad nationwide ZIP range stops the host at startup.
        services.AddOptions<RoutingOptions>()
            .Bind(configuration.GetSection(RoutingOptions.Name))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RoutingOptions>, RoutingOptionsValidator>();

        // Plan-search guard rails (NFR-05). Invalid limits stop the host at startup.
        services.AddOptions<PlanSearchOptions>()
            .Bind(configuration.GetSection(PlanSearchOptions.Name))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PlanSearchOptions>, PlanSearchOptionsValidator>();

        // The caller hasher (OR-027): HMAC-SHA256 with CallerHashing:Secret. The secret comes from the environment or
        // user-secrets, never from a committed file; a missing secret stops the host at startup.
        services.AddOptions<CallerHashingOptions>()
            .Bind(configuration.GetSection(CallerHashingOptions.Name))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CallerHashingOptions>, CallerHashingOptionsValidator>();
        services.AddSingleton<ICallerHasher, HmacCallerHasher>();

        services.AddSingleton<IRandomSource, SystemRandomSource>();

        // The experiment assigner (OR-016): the port is in Domain, the reloadable-configuration adapter in Infrastructure.
        services.AddOrderRouterExperiments();

        services.AddSingleton<IPlanSearchService, PlanSearchService>();
        services.AddSingleton<IRoutingPolicyFactory, RoutingPolicyFactory>();
        services.AddSingleton<ITieBreaker, UniformRandomTieBreaker>();
        services.AddSingleton(provider => new SupplierEligibility(ZipRange.Parse(provider.GetRequiredService<IOptions<RoutingOptions>>().Value.NationwideZipRange)));
        services.AddSingleton<OrderRoutingService>();

        // The decision log (AB-04): one event per stored order. It only needs an ILogger, so it is a singleton.
        services.AddSingleton<IRoutingDecisionLog, LoggerRoutingDecisionLog>();

        // The creation time of a stored order; tests replace it to fix the clock (OR-027).
        services.TryAddSingleton(TimeProvider.System);

        // Scoped, like the EF providers they depend on.
        services.AddScoped<IRouteOrderService, RouteOrderService>();
        services.AddScoped<IGetStoredOrderService, GetStoredOrderService>();

        return services;
    }

    /// <summary>
    /// Registers the health checks: a connectivity-only database check, plus the startup check that exits the host
    /// when PostgreSQL is unreachable (SC-23, SC-33).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection ConfigureHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHealthChecks().AddDbContextCheck<OrderRouterDataContext>(
            name: DatabaseHealthCheckName,
            failureStatus: HealthStatus.Unhealthy,
            tags: [HealthTags.Dependency],
            customTestQuery: CanConnectWithinTimeoutAsync);

        // Same check as GET /health, run once at startup. Connectivity only: an empty or unmigrated database starts normally.
        services.AddHostedService<DependencyStartupCheck>();

        return services;
    }

    private static async Task<bool> CanConnectWithinTimeoutAsync(OrderRouterDataContext context, CancellationToken ct)
    {
        // A hung server must not hang the endpoint: bound the probe, and let the timeout surface as Unhealthy.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(healthCheckTimeout);

        return await context.Database.CanConnectAsync(timeout.Token);
    }
}
