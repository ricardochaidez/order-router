using OrderRouter.Domain.Services;
using OrderRouter.Infrastructure.Services;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using OrderRouter.Api.Security;
using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Services.Policies;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>
/// OR-016 acceptance on the real host: the real configuration pipeline (a temp <c>experiments.json</c> that reloads),
/// the real JWT/dev-key principals and the real assigner. A recording policy factory captures the assignment each
/// request got (treatment policies do not exist until OR-017..019) and builds the control policies.
/// </summary>
public class ExperimentAssignmentHostTests(ReferenceDataFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(30);

    private static string Body(string orderId = "ORD-001", string zip = "10015")
        => $$"""
            { "order_id": "{{orderId}}", "customer_zip": "{{zip}}", "mail_order": false,
              "items": [ { "product_code": "WC-STD-001", "quantity": 1 } ] }
            """;

    private static string Exp2(bool enabled, int percentage, string callers = "", string prefixes = "", string ranges = "", string salt = "exp2")
        => $$"""
            {
              "Experiments": {
                "Exp2UnratedSuppliers": {
                  "Enabled": {{(enabled ? "true" : "false")}}, "TreatmentPercentage": {{percentage}}, "Salt": "{{salt}}",
                  "Targeting": { "Callers": [{{callers}}], "ZipPrefixes": [{{prefixes}}], "ZipRanges": [{{ranges}}] }
                }
              }
            }
            """;

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string because)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for: {because}");
            await Task.Delay(100, Token);
        }
    }

    private sealed class TempExperimentsFile : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("experiments-").FullName;

        public TempExperimentsFile(string json) => Write(json);

        public IFileProvider Provider => new PhysicalFileProvider(directory);

        public void Write(string json) => File.WriteAllText(Path.Combine(directory, "experiments.json"), json);

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }

    private sealed class RecordingPolicyFactory : IRoutingPolicyFactory
    {
        private readonly ConcurrentQueue<ExperimentAssignment> assignments = new();

        public ExperimentAssignment Last => assignments.TryPeek(out _) ? assignments.Last() : throw new InvalidOperationException("No request reached the policy factory.");

        public RoutingPolicy Create(ExperimentAssignment assignment)
        {
            assignments.Enqueue(assignment);

            return new RoutingPolicyFactory(new UniformRandomTieBreaker(new SystemRandomSource())).Create(ExperimentAssignment.AllControl);
        }
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capturing(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class Capturing(string category, ConcurrentQueue<(string, LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Enqueue((category, logLevel, formatter(state, exception)));
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> factory;

        public Host(string connectionString, TempExperimentsFile? file, string? devApiKey = null)
        {
            var baseFactory = AuthApiFactory.Create(
                connectionString,
                devApiKey: devApiKey,
                configureLogging: logging => logging.AddProvider(Logs),
                configureServices: services =>
                {
                    services.RemoveAll<IRoutingPolicyFactory>();
                    services.AddSingleton<IRoutingPolicyFactory>(Recorder);
                });

            factory = file is null
                ? baseFactory
                : baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                    config.AddJsonFile(file.Provider, "experiments.json", optional: false, reloadOnChange: true)));
        }

        public RecordingPolicyFactory Recorder { get; } = new();

        public CapturingProvider Logs { get; } = new();

        public HttpClient Client => factory.CreateClient();

        public IServiceProvider Services => factory.Services;

        public int Errors => Logs.Entries.Count(entry => entry.Level == LogLevel.Error && entry.Message.Contains("rejected", StringComparison.Ordinal));

        public async Task<HttpStatusCode> PostAsync(string body, string? token = null, string? apiKey = null)
        {
            using var request = RouteApiFactory.Post("/api/v1/route", body, authenticated: apiKey is null, token: token);

            if (apiKey is not null)
            {
                request.Headers.Add(DevApiKeyDefaults.HeaderName, apiKey);
            }

            using var response = await Client.SendAsync(request, Token);

            return response.StatusCode;
        }

        public async Task<ExperimentAssignment> AssignmentForAsync(string body, string? token = null, string? apiKey = null)
        {
            Assert.Equal(HttpStatusCode.Created, await PostAsync(body, token, apiKey));

            return Recorder.Last;
        }

        public ValueTask DisposeAsync() => factory.DisposeAsync();
    }

    [Fact]
    public async Task Post_ShippedConfig_EveryExperimentIsControlAndDisabled()
    {
        // AC1
        await using var host = new Host(fixture.ConnectionString, file: null);

        var assignment = await host.AssignmentForAsync(Body());

        Assert.Equal(ExperimentArms.AllControl, assignment.Arms);
        Assert.Equal(ExperimentReasons.AllDisabled, assignment.Reasons);
        Assert.NotEqual(ExperimentAssignment.DefaultConfigVersion, assignment.ConfigVersion);
        Assert.Equal(12, assignment.ConfigVersion.Length);
    }

    [Fact]
    public async Task Post_FileEditedWhileRunning_NextRequestUsesTheNewArmsAndVersion()
    {
        // AC6: no restart.
        using var file = new TempExperimentsFile(Exp2(enabled: false, percentage: 0));
        await using var host = new Host(fixture.ConnectionString, file);
        var before = await host.AssignmentForAsync(Body());

        file.Write(Exp2(enabled: true, percentage: 100));

        await WaitUntilAsync(async () => (await host.AssignmentForAsync(Body())).Arms.Exp2UnratedSuppliers == ExperimentArm.Treatment, "the edit to be applied");
        var after = host.Recorder.Last;

        Assert.Equal(ExperimentArm.Control, before.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.Bucketed, after.Reasons.Exp2UnratedSuppliers);
        Assert.NotEqual(before.ConfigVersion, after.ConfigVersion);
    }

    [Fact]
    public async Task Post_InvalidEdits_KeepTheLastKnownGoodAndLogAnError()
    {
        // AC7 and AC12
        using var file = new TempExperimentsFile(Exp2(enabled: true, percentage: 100));
        await using var host = new Host(fixture.ConnectionString, file);
        var good = await host.AssignmentForAsync(Body());

        file.Write(Exp2(enabled: true, percentage: 150));
        await WaitUntilAsync(() => Task.FromResult(host.Errors >= 1), "the percentage 150 to be rejected");
        var afterPercentage = await host.AssignmentForAsync(Body());

        var errorsBefore = host.Errors;
        file.Write(Exp2(enabled: true, percentage: 100, ranges: "\"2100-2199\""));
        await WaitUntilAsync(() => Task.FromResult(host.Errors > errorsBefore), "the 4-digit range to be rejected");
        var afterRange = await host.AssignmentForAsync(Body());

        Assert.Equal(good, afterPercentage);
        Assert.Equal(good, afterRange);
        Assert.Contains(host.Logs.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains(good.ConfigVersion, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_CallerTargeting_OnlyTheListedJwtSubjectIsTreated()
    {
        // AC9
        using var file = new TempExperimentsFile(Exp2(enabled: true, percentage: 100, callers: "\"portal-client\""));
        await using var host = new Host(fixture.ConnectionString, file);

        var listed = await host.AssignmentForAsync(Body(), TestJwt.ForSubject("portal-client"));
        var other = await host.AssignmentForAsync(Body(), TestJwt.ForSubject("other-client"));

        Assert.Equal(ExperimentArm.Treatment, listed.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.Bucketed, listed.Reasons.Exp2UnratedSuppliers);
        Assert.Equal(ExperimentArm.Control, other.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.NotTargeted, other.Reasons.Exp2UnratedSuppliers);
    }

    [Theory]
    [InlineData("client_id")]
    [InlineData("azp")]
    public async Task Post_NoSub_CallerComesFromClientIdOrAzp(string claim)
    {
        using var file = new TempExperimentsFile(Exp2(enabled: true, percentage: 100, callers: "\"portal-client\""));
        await using var host = new Host(fixture.ConnectionString, file);

        var assignment = await host.AssignmentForAsync(Body(), TestJwt.WithClaims((claim, "portal-client")));

        Assert.Equal(ExperimentArm.Treatment, assignment.Arms.Exp2UnratedSuppliers);
    }

    [Fact]
    public async Task Post_ZipRange_OnlyZipsInsideTheRangeAreTreated()
    {
        // AC10
        using var file = new TempExperimentsFile(Exp2(enabled: true, percentage: 100, ranges: "\"30000-30999\""));
        await using var host = new Host(fixture.ConnectionString, file);

        var inside = await host.AssignmentForAsync(Body(zip: "30500"));
        var outside = await host.AssignmentForAsync(Body(zip: "31000"));

        Assert.Equal(ExperimentArm.Treatment, inside.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExperimentArm.Control, outside.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.NotTargeted, outside.Reasons.Exp2UnratedSuppliers);
    }

    [Fact]
    public async Task Post_TokenWithNoCallerClaim_BucketsOnTheOrderId()
    {
        // AC11
        using var file = new TempExperimentsFile(Exp2(enabled: true, percentage: 50));
        await using var host = new Host(fixture.ConnectionString, file);
        var token = TestJwt.WithoutCaller();

        var arms = new Dictionary<string, ExperimentArm>();

        foreach (var orderId in Enumerable.Range(0, 30).Select(i => $"ORD-{i}"))
        {
            var first = (await host.AssignmentForAsync(Body(orderId), token)).Arms.Exp2UnratedSuppliers;
            var second = (await host.AssignmentForAsync(Body(orderId), token)).Arms.Exp2UnratedSuppliers;

            Assert.Equal(first, second);
            arms[orderId] = first;
        }

        Assert.Contains(ExperimentArm.Treatment, arms.Values);
        Assert.Contains(ExperimentArm.Control, arms.Values);
    }

    [Fact]
    public async Task Post_DevKey_CallerIsDevKey()
    {
        using var file = new TempExperimentsFile(Exp2(enabled: true, percentage: 100, callers: $"\"{DevApiKeyDefaults.CallerId}\""));
        await using var host = new Host(fixture.ConnectionString, file, devApiKey: "local-dev-key");

        var assignment = await host.AssignmentForAsync(Body(), apiKey: "local-dev-key");

        Assert.Equal(ExperimentArm.Treatment, assignment.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.Bucketed, assignment.Reasons.Exp2UnratedSuppliers);
    }

    [Fact]
    public async Task Host_ExperimentsFileSource_SitsBeforeEnvironmentVariablesAndTheCommandLine()
    {
        // ADR-0005: environment variables and the command line must still override experiments.json.
        await using var host = new Host(fixture.ConnectionString, file: null);
        _ = host.Client;
        var providers = ((IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>()).Providers.ToList();

        var experiments = providers.FindIndex(provider => provider is Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider json && json.Source.Path == "experiments.json");
        var environment = providers.FindIndex(provider => provider is Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationProvider);
        var commandLine = providers.FindIndex(provider => provider is Microsoft.Extensions.Configuration.CommandLine.CommandLineConfigurationProvider);

        Assert.True(experiments >= 0, "experiments.json is not a configuration source.");
        Assert.True(environment > experiments, "environment variables must come after experiments.json.");
        Assert.True(commandLine < 0 || commandLine > experiments, "the command line must come after experiments.json.");
    }

    [Fact]
    public async Task Startup_InvalidExperimentsFile_FailsToStart()
    {
        using var file = new TempExperimentsFile(Exp2(enabled: true, percentage: 150));
        await using var host = new Host(fixture.ConnectionString, file);

        var exception = Record.Exception(() => host.Client);

        Assert.NotNull(exception);
        Assert.Contains("between 0 and 100", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_MalformedExperimentsFile_FailsToStart()
    {
        using var file = new TempExperimentsFile("{ this is not json");
        await using var host = new Host(fixture.ConnectionString, file);

        Assert.NotNull(Record.Exception(() => host.Client));
    }

    [Fact]
    public async Task Post_FileBecomesMalformedWhileRunning_HostKeepsServingTheLastKnownGood()
    {
        // The file provider fails the reload before it notifies anyone, so no listener runs and the snapshot stays.
        using var file = new TempExperimentsFile(Exp2(enabled: true, percentage: 100));
        await using var host = new Host(fixture.ConnectionString, file);
        var good = await host.AssignmentForAsync(Body());

        file.Write("{ this is not json");
        await Task.Delay(TimeSpan.FromSeconds(2), Token);

        Assert.Equal(good, await host.AssignmentForAsync(Body()));
    }
}
