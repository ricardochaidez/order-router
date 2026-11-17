using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderRouter.Infrastructure;

namespace OrderRouter.Api.IntegrationTests.Infrastructure;

/// <summary>
/// A uniquely named database on the compose PostgreSQL server, dropped on dispose, so schema tests
/// never touch the shared <c>orderrouter</c> database and a failed test leaves nothing behind.
/// </summary>
public sealed class ThrowawayDatabase : IAsyncDisposable
{
    private readonly NpgsqlConnectionStringBuilder serverConnection;

    private ThrowawayDatabase(NpgsqlConnectionStringBuilder serverConnection, string name)
    {
        this.serverConnection = serverConnection;
        Name = name;
        ConnectionString = new NpgsqlConnectionStringBuilder(serverConnection.ConnectionString) { Database = name }.ConnectionString;
    }

    /// <summary>The generated database name.</summary>
    public string Name { get; }

    /// <summary>Connection string for the throwaway database.</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Creates the database. Fails (never skips) with a clear message when PostgreSQL is unreachable.
    /// </summary>
    public static async Task<ThrowawayDatabase> CreateAsync(CancellationToken ct)
    {
        var configured = Environment.GetEnvironmentVariable(OrderRouterDataContextFactory.ConnectionStringVariable);
        var server = new NpgsqlConnectionStringBuilder(
            string.IsNullOrWhiteSpace(configured) ? OrderRouterDataContextFactory.DefaultConnectionString : configured)
        {
            // The maintenance database is always present, so CREATE DATABASE can run from it.
            Database = "postgres",
            Timeout = 5,
        };

        var name = $"or_schema_test_{Guid.NewGuid():N}";

        try
        {
            await using var connection = new NpgsqlConnection(server.ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (NpgsqlException exception)
        {
            throw new InvalidOperationException(
                "PostgreSQL is not reachable for the schema tests. Start it with `docker compose up -d` " +
                $"(or set {OrderRouterDataContextFactory.ConnectionStringVariable}). Cause: {exception.Message}",
                exception);
        }

        return new ThrowawayDatabase(server, name);
    }

    /// <summary>Creates a context on the throwaway database.</summary>
    public OrderRouterDataContext CreateContext()
        => new(new DbContextOptionsBuilder<OrderRouterDataContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>Runs a query and returns the first column of each row as text.</summary>
    public async Task<List<string>> QueryAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<string>();

        while (await reader.ReadAsync(ct))
        {
            rows.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return rows;
    }

    /// <summary>Runs a non-query statement.</summary>
    public async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Drops the database, closing any pooled connections first.</summary>
    public async ValueTask DisposeAsync()
    {
        // Pooled connections would otherwise keep the database busy; FORCE also kicks any stragglers.
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(serverConnection.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {Name} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
