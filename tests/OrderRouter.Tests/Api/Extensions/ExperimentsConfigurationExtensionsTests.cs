using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using OrderRouter.Api.Extensions;

namespace OrderRouter.Tests.Api.Extensions;

public sealed class ExperimentsConfigurationExtensionsTests : IDisposable
{
    private readonly DirectoryInfo contentRoot = Directory.CreateTempSubdirectory("experiments-config-");

    public void Dispose() => contentRoot.Delete(recursive: true);

    // The real host adds ASPNETCORE_ and DOTNET_ environment-variable sources ahead of appsettings.json. The file must
    // land after appsettings*.json and just before the unprefixed environment variables, not before the prefixed ones.
    [Fact]
    public void AddExperimentsFile_RealHost_PlacesTheFileAfterAppSettingsAndJustBeforeTheUnprefixedEnvironment()
    {
        var builder = CreateBuilder();

        builder.Configuration.AddExperimentsFile();

        var sources = builder.Configuration.Sources.ToList();
        var experiments = sources.FindIndex(source => IsJson(source, ExperimentsConfigurationExtensions.FileName));
        var lastAppSettings = sources.FindLastIndex(source => source is JsonConfigurationSource { Path: { } path } && path.StartsWith("appsettings", StringComparison.Ordinal));
        var unprefixedEnvironment = sources.FindIndex(source => source is EnvironmentVariablesConfigurationSource { Prefix: null or "" });

        Assert.True(lastAppSettings >= 0, "The host should add appsettings.json.");
        Assert.True(experiments > lastAppSettings, $"experiments.json is at {experiments}, before appsettings at {lastAppSettings}.");
        Assert.Equal(experiments + 1, unprefixedEnvironment);
    }

    [Fact]
    public void AddExperimentsFile_KeyInBothFiles_ExperimentsFileWins()
    {
        File.WriteAllText(Path.Combine(contentRoot.FullName, "appsettings.json"), """{ "Routing": { "SimilarityBand": 9 } }""");
        File.WriteAllText(Path.Combine(contentRoot.FullName, ExperimentsConfigurationExtensions.FileName), """{ "Routing": { "SimilarityBand": 0.5 } }""");
        var builder = CreateBuilder();

        builder.Configuration.AddExperimentsFile();

        Assert.Equal("0.5", builder.Configuration["Routing:SimilarityBand"]);
    }

    [Fact]
    public void AddExperimentsFile_NoUnprefixedEnvironmentSource_AppendsTheFile()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables("ASPNETCORE_");

        configuration.AddExperimentsFile();

        Assert.True(IsJson(configuration.Sources[^1], ExperimentsConfigurationExtensions.FileName));
    }

    private WebApplicationBuilder CreateBuilder()
        => WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = contentRoot.FullName, EnvironmentName = "Production" });

    private static bool IsJson(IConfigurationSource source, string path) => source is JsonConfigurationSource { Path: { } actual } && actual == path;
}
