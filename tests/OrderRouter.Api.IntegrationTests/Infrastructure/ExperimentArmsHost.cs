using System.Globalization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using OrderRouter.Domain.Interfaces;

namespace OrderRouter.Api.IntegrationTests.Infrastructure;

/// <summary>One arm choice per experiment: <c>true</c> is treatment, <c>false</c> is control.</summary>
internal readonly record struct ExperimentCombination(bool Exp1, bool Exp2, bool Exp3)
{
    /// <summary>Every combination in the order of the OR-024 table (EXP-3 changes fastest).</summary>
    public static IEnumerable<(int Number, ExperimentCombination Combination)> All()
    {
        for (var index = 0; index < 8; index++)
        {
            yield return (index + 1, new ExperimentCombination((index & 4) != 0, (index & 2) != 0, (index & 1) != 0));
        }
    }

    /// <summary>The arm name the diagnostics report for an experiment.</summary>
    public static string ArmName(bool treatment) => treatment ? "treatment" : "control";
}

/// <summary>
/// Builds the real API host with the three experiments pinned to one combination and a fixed random source (OR-024 AC1).
/// Treatment is "enabled at 100%, no targeting"; control is "disabled". It goes through the same temp
/// <c>experiments.json</c> + <c>AddJsonFile</c> route as the OR-017 tests, and diagnostics are on so a test can verify the arms.
/// </summary>
internal sealed class ExperimentArmsHost : IAsyncDisposable
{
    private readonly TempExperimentsFile file;

    private ExperimentArmsHost(TempExperimentsFile file, WebApplicationFactory<Program> factory, FixedRandomSource random)
    {
        this.file = file;
        Factory = factory;
        Random = random;
    }

    /// <summary>The pinned host.</summary>
    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>The random source every tie-break used.</summary>
    public FixedRandomSource Random { get; }

    /// <summary>Creates the host over <paramref name="connectionString"/> with <paramref name="combination"/> pinned.</summary>
    /// <param name="connectionString">The database to route against.</param>
    /// <param name="combination">The arms to pin.</param>
    /// <param name="randomValue">The index the fixed random source returns for a tie.</param>
    public static ExperimentArmsHost Create(string connectionString, ExperimentCombination combination, int randomValue = 0)
    {
        var file = new TempExperimentsFile(Json(combination));
        var random = new FixedRandomSource(randomValue);
        var factory = RouteApiFactory.Create(connectionString, useCase: null, exposeDiagnostics: true, experimentsFile: file.Provider)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRandomSource>();
                services.AddSingleton<IRandomSource>(random);
            }));

        return new ExperimentArmsHost(file, factory, random);
    }

    /// <summary>The <c>experiments.json</c> for a combination, with the shipped default parameters.</summary>
    public static string Json(ExperimentCombination combination)
        => $$"""
            { "Experiments": {
                "Exp1ConsolidationVsQuality": {{Definition(combination.Exp1, "exp1", """ "Parameters": { "QualityUpliftThreshold": 2.0 } """)}},
                "Exp2UnratedSuppliers": {{Definition(combination.Exp2, "exp2", """ "Parameters": { "UnratedPrior": 6.5 } """)}},
                "Exp3PartialRouting": {{Definition(combination.Exp3, "exp3", null)}} },
              "Routing": { "SimilarityBand": 0.5 } }
            """;

    private static string Definition(bool treatment, string salt, string? parameters)
        => $$"""
            { "Enabled": {{(treatment ? "true" : "false")}}, "TreatmentPercentage": {{(treatment ? 100 : 0).ToString(CultureInfo.InvariantCulture)}}, "Salt": "{{salt}}",
              "Targeting": { "Callers": [], "ZipPrefixes": [], "ZipRanges": [] }{{(parameters is null ? string.Empty : "," + parameters)}} }
            """;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        file.Dispose();
    }
}
