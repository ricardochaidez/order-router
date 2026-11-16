using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Infrastructure.Persistence.ReferenceData;

namespace OrderRouter.Infrastructure.Persistence;

/// <summary>Dependency-injection registration for the PostgreSQL persistence layer.</summary>
public static class PersistenceServiceCollectionExtensions
{
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

        return services;
    }
}
