namespace OrderRouter.Api.Authentication;

/// <summary>
/// Logs one warning at startup when a dev key is configured outside Development, where it is ignored. The value
/// is never logged (NFR-11).
/// </summary>
internal sealed partial class DevApiKeyIgnoredNotice(ILogger<DevApiKeyIgnoredNotice> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogIgnored(logger, DevApiKeyDefaults.ConfigurationKey);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Setting} is configured but ignored: the development key only works in the Development environment.")]
    private static partial void LogIgnored(ILogger logger, string setting);
}
