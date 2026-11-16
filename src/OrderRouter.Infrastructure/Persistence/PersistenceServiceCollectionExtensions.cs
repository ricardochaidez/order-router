using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderRouter.Application.Routing.Storage;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Infrastructure.Persistence.ReferenceData;
using OrderRouter.Infrastructure.Persistence.RoutingOrders;

namespace OrderRouter.Infrastructure.Persistence;

/// <summary>Dependency-injection registration for the PostgreSQL persistence layer.</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Name the PostgreSQL health check is registered under.</summary>
    public const string DatabaseHealthCheckName = "postgresql";

    private static readonly TimeSpan healthCheckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Registers <see cref="OrderRouterDbContext"/> against PostgreSQL, plus the read-only reference-data ports
    /// <see cref="ICapabilityResolver"/> and <see cref="IReferenceDataReader"/> (OR-006). It never opens a connection
    /// and never applies migrations: migrating is an explicit step, not a startup side effect (SC-22, SC-33).
    /// The seeder is deliberately not registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">Npgsql connection string, normally <c>ConnectionStrings:OrderRouter</c>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty or whitespace.</exception>
    /// <example>
    /// <code>
    /// builder.Services.AddOrderRouterDatabase(builder.Configuration.GetConnectionString("OrderRouter")!);
    /// </code>
    /// </example>
    public static IServiceCollection AddOrderRouterDatabase(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<OrderRouterDbContext>(options => options.UseNpgsql(connectionString));

        // Scoped, to match the DbContext lifetime they depend on.
        services.AddScoped<ICapabilityResolver, EfCapabilityResolver>();
        services.AddScoped<IReferenceDataReader, EfReferenceDataReader>();

        // The only writer the API has: it inserts into the stored-order tables, never into reference data (ADR-0007).
        services.AddScoped<IRoutingOrderStore, EfRoutingOrderStore>();

        // The read side of the same tables (OR-028): reads only, filtered by the caller's hash.
        services.AddScoped<IRoutingOrderReader, EfRoutingOrderReader>();

        return services;
    }

    /// <summary>
    /// Adds the PostgreSQL reachability check, named <c>postgresql</c> and tagged <see cref="HealthTags.Dependency"/>.
    /// It only opens a connection (<c>CanConnectAsync</c>); it never queries a table, so an empty or unmigrated
    /// database is healthy (SC-23, SC-33, ADR-0002). A server that does not answer within five seconds is unhealthy.
    /// </summary>
    /// <param name="builder">The health-checks builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddHealthChecks().AddOrderRouterDatabaseHealthCheck();
    /// </code>
    /// </example>
    public static IHealthChecksBuilder AddOrderRouterDatabaseHealthCheck(this IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddDbContextCheck<OrderRouterDbContext>(
            name: DatabaseHealthCheckName,
            failureStatus: HealthStatus.Unhealthy,
            tags: [HealthTags.Dependency],
            customTestQuery: CanConnectWithinTimeoutAsync);
    }

    private static async Task<bool> CanConnectWithinTimeoutAsync(OrderRouterDbContext context, CancellationToken cancellationToken)
    {
        // A hung server must not hang the endpoint: bound the probe, and let the timeout surface as Unhealthy.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(healthCheckTimeout);

        return await context.Database.CanConnectAsync(timeout.Token);
    }
}
