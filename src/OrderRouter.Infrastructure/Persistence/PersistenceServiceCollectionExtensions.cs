using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OrderRouter.Infrastructure.Persistence;

/// <summary>Dependency-injection registration for the PostgreSQL persistence layer.</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="OrderRouterDbContext"/> against PostgreSQL. It never opens a connection
    /// and never applies migrations: migrating is an explicit step, not a startup side effect (SC-22, SC-33).
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

        return services.AddDbContext<OrderRouterDbContext>(options => options.UseNpgsql(connectionString));
    }
}
