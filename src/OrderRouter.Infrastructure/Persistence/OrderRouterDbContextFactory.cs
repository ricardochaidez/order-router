using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderRouter.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> at design time (migrations add / script / database update).
/// The running API never uses it and never migrates (SC-22, SC-33).
/// </summary>
public sealed class OrderRouterDbContextFactory : IDesignTimeDbContextFactory<OrderRouterDbContext>
{
    /// <summary>Environment variable that overrides the connection string (ADR-0002).</summary>
    public const string ConnectionStringVariable = "ConnectionStrings__OrderRouter";

    /// <summary>Matches the defaults in <c>docker-compose.yml</c>; dev-only credentials.</summary>
    public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=orderrouter;Username=orderrouter;Password=orderrouter-dev-only";

    /// <inheritdoc />
    public OrderRouterDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DefaultConnectionString;
        }

        var options = new DbContextOptionsBuilder<OrderRouterDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OrderRouterDbContext(options);
    }
}
