using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OrderRouter.Api.Health;
using OrderRouter.Infrastructure;

namespace OrderRouter.UnitTests.Api.Health;

public class DependencyStartupCheckTests
{
    private const string secret = "Password=super-secret";

    [Fact]
    public async Task StartAsync_AllChecksHealthy_Completes()
    {
        var service = new StubHealthCheckService(HealthStatus.Healthy);
        var check = new DependencyStartupCheck(service, new ListLogger());

        await check.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(service.LastPredicateWasDependencyOnly);
    }

    [Theory]
    [InlineData(HealthStatus.Unhealthy)]
    [InlineData(HealthStatus.Degraded)]
    public async Task StartAsync_CheckNotHealthy_ThrowsNamingTheCheck(HealthStatus status)
    {
        var check = new DependencyStartupCheck(new StubHealthCheckService(status), new ListLogger());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal($"Dependency check failed at startup: postgresql is {status}.", exception.Message);
    }

    [Fact]
    public async Task StartAsync_CheckFailsWithSecretInException_NeitherMessageNorLogsLeakIt()
    {
        var logger = new ListLogger();
        var check = new DependencyStartupCheck(new StubHealthCheckService(HealthStatus.Unhealthy), logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.NotEmpty(logger.Messages);
        Assert.All(logger.Messages, message => Assert.DoesNotContain(secret, message, StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("postgresql", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopAsync_Always_Completes()
    {
        var check = new DependencyStartupCheck(new StubHealthCheckService(HealthStatus.Healthy), new ListLogger());

        await check.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class StubHealthCheckService(HealthStatus status) : HealthCheckService
    {
        public bool LastPredicateWasDependencyOnly { get; private set; }

        public override Task<HealthReport> CheckHealthAsync(Func<HealthCheckRegistration, bool>? predicate, CancellationToken cancellationToken = default)
        {
            var dependency = new HealthCheckRegistration("postgresql", new NoopCheck(), null, [HealthTags.Dependency]);
            var other = new HealthCheckRegistration("other", new NoopCheck(), null, ["other"]);
            LastPredicateWasDependencyOnly = predicate is not null && predicate(dependency) && !predicate(other);

            var entry = new HealthReportEntry(status, null, TimeSpan.Zero, new InvalidOperationException(secret), null);

            return Task.FromResult(new HealthReport(new Dictionary<string, HealthReportEntry> { ["postgresql"] = entry }, TimeSpan.Zero));
        }
    }

    private sealed class NoopCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(HealthCheckResult.Healthy());
    }

    private sealed class ListLogger : ILogger<DependencyStartupCheck>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add($"{formatter(state, exception)} {exception}");
    }
}
