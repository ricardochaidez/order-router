using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OrderRouter.Infrastructure;

namespace OrderRouter.Api.Health;

/// <summary>
/// Fails the host fast when a required dependency is not reachable at startup, so a mis-wired container exits
/// instead of serving 503s forever. It reuses the same dependency checks as <c>GET /health</c>, so "reachable"
/// has one definition. It only opens a connection; it never reads or migrates data (SC-22, SC-23, SC-33).
/// </summary>
internal sealed partial class DependencyStartupCheck(HealthCheckService healthChecks, ILogger<DependencyStartupCheck> logger) : IHostedService
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">A dependency check is not healthy.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(registration => registration.Tags.Contains(HealthTags.Dependency), cancellationToken);

        if (report.Status is HealthStatus.Healthy)
        {
            return;
        }

        var failing = report.Entries.Where(entry => entry.Value.Status is not HealthStatus.Healthy).ToArray();

        foreach (var (name, entry) in failing)
        {
            // Name and status only: the exception can carry the connection string (NFR-11).
            LogDependencyFailed(name, entry.Status);
        }

        var summary = string.Join(", ", failing.Select(entry => $"{entry.Key} is {entry.Value.Status}"));

        throw new InvalidOperationException($"Dependency check failed at startup: {summary}.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Error, Message = "Dependency check {CheckName} is {Status} at startup.")]
    private partial void LogDependencyFailed(string checkName, HealthStatus status);
}
