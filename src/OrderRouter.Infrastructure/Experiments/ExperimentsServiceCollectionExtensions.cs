using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using OrderRouter.Application.Routing.Experiments;

namespace OrderRouter.Infrastructure.Experiments;

/// <summary>Dependency-injection registration for the configuration-based experiment assigner (OR-016).</summary>
public static class ExperimentsServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="ExperimentsOptions"/> to the configuration root, registers the assigner as the one
    /// <see cref="IExperimentAssigner"/>, and adds a startup check so invalid configuration stops the host.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddOrderRouterExperiments(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<ExperimentsOptions>();
        services.AddSingleton<IConfigureOptions<ExperimentsOptions>, BindRootConfiguration>();
        services.AddSingleton<IOptionsChangeTokenSource<ExperimentsOptions>, RootChangeTokenSource>();
        services.AddSingleton<ExperimentsOptionsValidator>();
        services.AddSingleton<IExperimentAssigner, ConfigurationExperimentAssigner>();
        services.AddHostedService<ExperimentsStartupCheck>();

        return services;
    }

    // Binds the whole root. A bind failure is recorded instead of thrown: the monitor runs this inside its reload
    // callback, where an exception would escape onto the file-watcher thread (see ExperimentsOptionsValidator).
    private sealed class BindRootConfiguration(IConfiguration configuration) : IConfigureOptions<ExperimentsOptions>
    {
        public void Configure(ExperimentsOptions options)
        {
            try
            {
                configuration.Bind(options);
            }
            catch (InvalidOperationException exception)
            {
                options.BindError = $"The experiment configuration could not be read: {exception.Message}";
            }
        }
    }

    private sealed class RootChangeTokenSource(IConfiguration configuration) : IOptionsChangeTokenSource<ExperimentsOptions>
    {
        public string Name => Options.DefaultName;

        public IChangeToken GetChangeToken() => configuration.GetReloadToken();
    }

    // Validates the initial configuration (the ValidateOnStart behaviour of ADR-0005) and builds the assigner now,
    // so a bad file fails at startup instead of on the first request.
    private sealed class ExperimentsStartupCheck(
        IOptionsMonitor<ExperimentsOptions> monitor,
        ExperimentsOptionsValidator validator,
        IExperimentAssigner assigner) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = assigner;

            var result = validator.Validate(Options.DefaultName, monitor.CurrentValue);

            return result.Failed
                ? throw new OptionsValidationException(Options.DefaultName, typeof(ExperimentsOptions), result.Failures)
                : Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
