using OrderRouter.Infrastructure.Persistence.Seeding;
using OrderRouter.IntegrationTests.Persistence;

namespace OrderRouter.IntegrationTests.Acceptance;

/// <summary>
/// OR-024: a throwaway database seeded with the committed seed files plus five test-only suppliers, so the matrix
/// has an EXP-2 and an EXP-1 x EXP-2 scenario without editing the shared seed (the sentinel data has no rated
/// competitor for the unrated suppliers). The extra suppliers sit in ZIPs 00050 and 00051, which no seeded supplier serves.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>00050</c>, rollator: SUP-X001 rated 6.0 and SUP-X002 unrated. Control picks X001 (6.0 against 0); treatment gives X002 the
/// prior 6.5, within the 0.5 band of 6.0 and equally local, so the higher quality wins.</item>
/// <item><c>00051</c>, wheelchair + cane: SUP-X011 unrated generalist, SUP-X012 and SUP-X013 rated 8.0 specialists. Against EXP-1 treatment
/// the one-shipment baseline is 0 in EXP-2 control (uplift 8.0, two shipments) and 6.5 in EXP-2 treatment (uplift 1.5 below 2.0, one shipment).</item>
/// </list>
/// </remarks>
public sealed class MatrixDatabaseFixture : IAsyncLifetime
{
    private static readonly string[] extraSuppliers =
    [
        "SUP-X001,Matrix Rated Rollator,00050,rollator,6,n",
        "SUP-X002,Matrix Unrated Rollator,00050,rollator,,n",
        "SUP-X011,Matrix Unrated Generalist,00051,\"cane, wheelchair\",,n",
        "SUP-X012,Matrix Specialist Wheelchair,00051,wheelchair,8,n",
        "SUP-X013,Matrix Specialist Cane,00051,cane,8,n",
    ];

    private ThrowawayDatabase? database;

    /// <summary>Connection string of the throwaway database.</summary>
    public string ConnectionString => database!.ConnectionString;

    /// <summary>Runs a query and returns the first column of each row as text.</summary>
    public Task<List<string>> QueryAsync(string sql, CancellationToken cancellationToken) => database!.QueryAsync(sql, cancellationToken);

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        database = await ThrowawayDatabase.CreateAsync(token);

        string[] suppliers = [.. (await File.ReadAllLinesAsync(SeedFiles.Real.SuppliersPath, token)).Skip(1), .. extraSuppliers];
        string[] products = [.. (await File.ReadAllLinesAsync(SeedFiles.Real.ProductsPath, token)).Skip(1)];
        using var files = SeedFiles.Create(suppliers, products);

        // The seeder applies the migrations itself (see ReferenceDataSeederTests).
        await using var context = database.CreateContext();
        await new ReferenceDataSeeder(context, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<ReferenceDataSeeder>.Instance)
            .SeedAsync(files.Files, token);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }
}
