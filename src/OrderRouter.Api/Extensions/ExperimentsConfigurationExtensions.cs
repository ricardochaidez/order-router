using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace OrderRouter.Api.Extensions;

/// <summary>Adds the reloadable experiment settings file (SC-25, ADR-0005) at the right precedence.</summary>
internal static class ExperimentsConfigurationExtensions
{
    /// <summary>The experiment settings file, relative to the content root.</summary>
    public const string FileName = "experiments.json";

    /// <summary>
    /// Adds <see cref="FileName"/> (optional, reloaded on change) just before the unprefixed environment-variable source.
    /// It then overrides <c>appsettings*.json</c> and user-secrets, and environment variables and the command line still
    /// override it. The host also adds <c>ASPNETCORE_</c> and <c>DOTNET_</c> environment-variable sources ahead of
    /// <c>appsettings.json</c>; matching one of those would give the file the lowest precedence of all, so they are skipped.
    /// </summary>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The same configuration, for chaining.</returns>
    public static IConfigurationBuilder AddExperimentsFile(this IConfigurationBuilder configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.AddJsonFile(FileName, optional: true, reloadOnChange: true);

        var sources = configuration.Sources;
        var experiments = sources[^1];
        sources.RemoveAt(sources.Count - 1);

        var environmentIndex = sources
            .ToList()
            .FindIndex(source => source is EnvironmentVariablesConfigurationSource { Prefix: null or "" });

        sources.Insert(environmentIndex < 0 ? sources.Count : environmentIndex, experiments);

        return configuration;
    }
}
