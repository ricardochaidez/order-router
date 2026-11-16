using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderRouter.Application.Routing.Experiments;
using OrderRouter.Domain.ReferenceData;
using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Infrastructure.Experiments;

namespace OrderRouter.UnitTests.Infrastructure.Experiments;

public class ConfigurationExperimentAssignerTests
{
    private static readonly AssignmentContext context = new("caller", ZipCode.Parse("10015"), "ORD-1");

    private readonly ListLogger logger = new();

    private static ExperimentsOptions Enabled(int percentage = 100, string salt = "exp2", decimal? prior = null)
    {
        var exp2 = new ExperimentOptions { Enabled = true, TreatmentPercentage = percentage, Salt = salt };
        exp2.Parameters.UnratedPrior = prior;

        return new ExperimentsOptions { Experiments = new ExperimentsSection { Exp2UnratedSuppliers = exp2 } };
    }

    private ConfigurationExperimentAssigner Assigner(TestOptionsMonitor<ExperimentsOptions> monitor)
        => new(monitor, logger);

    [Fact]
    public void Assign_DefaultOptions_IsAllControlDisabled()
    {
        // AC1
        using var assigner = Assigner(new TestOptionsMonitor<ExperimentsOptions>(new ExperimentsOptions()));

        var assignment = assigner.Assign(context);

        Assert.Equal(ExperimentArms.AllControl, assignment.Arms);
        Assert.Equal(ExperimentReasons.AllDisabled, assignment.Reasons);
    }

    [Fact]
    public void Constructor_InvalidInitialConfig_Throws()
    {
        var monitor = new TestOptionsMonitor<ExperimentsOptions>(Enabled(percentage: 150));

        Assert.Throws<OptionsValidationException>(() => Assigner(monitor));
    }

    [Fact]
    public void Assign_ValidChange_UsesTheNewArmsAndVersionOnTheNextCall()
    {
        // AC6
        var monitor = new TestOptionsMonitor<ExperimentsOptions>(new ExperimentsOptions());
        using var assigner = Assigner(monitor);
        var before = assigner.Assign(context);

        monitor.Change(Enabled());

        var after = assigner.Assign(context);

        Assert.Equal(ExperimentArm.Control, before.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExperimentArm.Treatment, after.Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.Bucketed, after.Reasons.Exp2UnratedSuppliers);
        Assert.NotEqual(before.ConfigVersion, after.ConfigVersion);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains(after.ConfigVersion));
    }

    [Fact]
    public void Assign_InvalidPercentage_KeepsTheLastKnownGoodAndLogsOneError()
    {
        // AC7
        var monitor = new TestOptionsMonitor<ExperimentsOptions>(Enabled());
        using var assigner = Assigner(monitor);
        var good = assigner.Assign(context);

        monitor.Change(Enabled(percentage: 150));

        var after = assigner.Assign(context);

        Assert.Equal(good, after);
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains(good.ConfigVersion, error.Message);
        Assert.Contains("between 0 and 100", error.Message);
    }

    [Fact]
    public void Assign_FourDigitRange_KeepsTheLastKnownGood()
    {
        // AC12
        var monitor = new TestOptionsMonitor<ExperimentsOptions>(Enabled());
        using var assigner = Assigner(monitor);
        var good = assigner.Assign(context);
        var bad = Enabled();
        bad.Experiments.Exp2UnratedSuppliers!.Targeting.ZipRanges = ["2100-2199"];

        monitor.Change(bad);

        Assert.Equal(good, assigner.Assign(context));
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public void Assign_InvalidThenValid_Recovers()
    {
        var monitor = new TestOptionsMonitor<ExperimentsOptions>(Enabled());
        using var assigner = Assigner(monitor);
        monitor.Change(Enabled(percentage: 150));

        monitor.Change(Enabled(percentage: 0));

        Assert.Equal(ExperimentArm.Control, assigner.Assign(context).Arms.Exp2UnratedSuppliers);
        Assert.Equal(ExposureReason.Bucketed, assigner.Assign(context).Reasons.Exp2UnratedSuppliers);
    }

    [Fact]
    public void Assign_ParametersComeFromTheConfiguration()
    {
        var options = Enabled(prior: 8m);
        options.Routing.SimilarityBand = 0.9m;
        using var assigner = Assigner(new TestOptionsMonitor<ExperimentsOptions>(options));

        var parameters = assigner.Assign(context).Parameters;

        Assert.Equal(8m, parameters.UnratedPrior);
        Assert.Equal(0.9m, parameters.SimilarityBand);
        Assert.Equal(RoutingParameters.DefaultQualityUpliftThreshold, parameters.QualityUpliftThreshold);
    }

    [Fact]
    public void Assign_AfterDispose_IgnoresFurtherChanges()
    {
        var monitor = new TestOptionsMonitor<ExperimentsOptions>(new ExperimentsOptions());
        var assigner = Assigner(monitor);

        assigner.Dispose();
        monitor.Change(Enabled());

        Assert.Equal(ExperimentArm.Control, assigner.Assign(context).Arms.Exp2UnratedSuppliers);
    }

    [Fact]
    public void Assign_NullContext_Throws()
    {
        using var assigner = Assigner(new TestOptionsMonitor<ExperimentsOptions>(new ExperimentsOptions()));

        Assert.Throws<ArgumentNullException>(() => assigner.Assign(null!));
    }

    [Fact]
    public void Logs_NeverContainTheCallerList()
    {
        var monitor = new TestOptionsMonitor<ExperimentsOptions>(new ExperimentsOptions());
        using var assigner = Assigner(monitor);
        var options = Enabled(percentage: 150);
        options.Experiments.Exp2UnratedSuppliers!.Targeting.Callers = ["secret-caller-id"];

        monitor.Change(options);
        var applied = Enabled();
        applied.Experiments.Exp2UnratedSuppliers!.Targeting.Callers = ["secret-caller-id"];
        monitor.Change(applied);

        Assert.All(logger.Entries, entry => Assert.DoesNotContain("secret-caller-id", entry.Message));
    }

    private sealed class ListLogger : ILogger<ConfigurationExperimentAssigner>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
