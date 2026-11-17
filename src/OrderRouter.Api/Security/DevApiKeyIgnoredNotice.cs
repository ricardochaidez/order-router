namespace OrderRouter.Api.Security;

/// <summary>
/// Logs one warning at startup when a dev key is configured outside Development, where it is ignored. The value
/// is never logged (NFR-11).
/// </summary>
internal sealed partial class DevApiKeyIgnoredNotice(ILogger<DevApiKeyIgnoredNotice> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        LogIgnored(logger, DevApiKeyDefaults.ConfigurationKey);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Setting} is configured but ignored: the development key only works in the Development environment.")]
    private static partial void LogIgnored(ILogger logger, string setting);
}
