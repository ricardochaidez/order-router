using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using OrderRouter.Api.IntegrationTests.Persistence;
using OrderRouter.Api.IntegrationTests;
using OrderRouter.Infrastructure.Services.Seeding;
using OrderRouter.Infrastructure;

[assembly: AssemblyFixture(typeof(ReferenceDataFixture))]

namespace OrderRouter.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Seeds the shared <c>orderrouter</c> database once, before any integration test runs, from the committed
/// normalized files. This is the only production-style caller of the seeder (SC-22, SC-33). If PostgreSQL
/// is unreachable or a file is bad it fails, so the test run fails; it is never skipped.
/// </summary>
public sealed class ReferenceDataFixture : IAsyncLifetime
{
    /// <summary>Connection string of the shared database (the same one the API uses).</summary>
    public string ConnectionString { get; } = ResolveConnectionString();

    /// <summary>What the seed run loaded.</summary>
    public ReferenceDataSeedSummary Summary { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<OrderRouterDataContext>().UseNpgsql(ConnectionString).Options;
        await using var context = new OrderRouterDataContext(options);
        var seeder = new ReferenceDataSeeder(context, TimeProvider.System, new DiagnosticLogger());

        try
        {
            Summary = await seeder.SeedAsync(SeedFiles.Real, TestContext.Current.CancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw new InvalidOperationException(
                "PostgreSQL is not reachable, so the reference data could not be seeded. Start it with `docker compose up -d` " +
                $"(or set {OrderRouterDataContextFactory.ConnectionStringVariable}). Cause: {exception.Message}",
                exception);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        return ValueTask.CompletedTask;
    }

    /// <summary>Runs a query on the shared database and returns the first column of each row as text.</summary>
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

    private static string ResolveConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable(OrderRouterDataContextFactory.ConnectionStringVariable);

        return string.IsNullOrWhiteSpace(configured) ? OrderRouterDataContextFactory.DefaultConnectionString : configured;
    }

    // Writes through xUnit diagnostics so the summary appears in `dotnet test` output (diagnosticMessages = true).
    private sealed class DiagnosticLogger : ILogger<ReferenceDataSeeder>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => TestContext.Current.SendDiagnosticMessage("[{0}] {1}", logLevel, formatter(state, exception));
    }
}
