using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Sections;
using OrderRouter.Infrastructure.Mappers;
using OrderRouter.Infrastructure.Services;

namespace OrderRouter.Tests.Infrastructure.Mappers;

public class ExperimentsOptionsMapperTests
{
    private static ExperimentOptions Experiment(bool enabled = true, int percentage = 50, string? salt = "salt") => new()
    {
        Enabled = enabled,
        TreatmentPercentage = percentage,
        Salt = salt,
    };

    private static ExperimentsOptions Options(ExperimentOptions? exp1 = null, ExperimentOptions? exp2 = null, ExperimentOptions? exp3 = null)
        => new() { Experiments = new ExperimentsSection { Exp1ConsolidationVsQuality = exp1, Exp2UnratedSuppliers = exp2, Exp3PartialRouting = exp3 } };

    private static IReadOnlyList<string> Errors(ExperimentsOptions options)
    {
        Assert.False(ExperimentMappingExtensions.TryBuild(options, out var configuration, out var errors));
        Assert.Null(configuration);

        return errors;
    }

    [Fact]
    public void TryBuild_ValidOptions_BuildsTheSnapshot()
    {
        var exp2 = Experiment(percentage: 20);
        exp2.Targeting = new ExperimentTargetingOptions { Callers = ["c"], ZipPrefixes = ["021"], ZipRanges = ["30000-30999"] };
        exp2.Parameters.UnratedPrior = 7m;
        var options = Options(exp2: exp2);
        options.Routing.SimilarityBand = 0.75m;

        Assert.True(ExperimentMappingExtensions.TryBuild(options, out var configuration, out var errors));

        Assert.Empty(errors);
        Assert.Equal(20, configuration.Exp2UnratedSuppliers.TreatmentPercentage);
        Assert.Equal(["c"], configuration.Exp2UnratedSuppliers.Targeting.Callers);
        Assert.Equal(ZipRange.Parse("30000-30999"), Assert.Single(configuration.Exp2UnratedSuppliers.Targeting.ZipRanges));
        Assert.Equal(7m, configuration.Parameters.UnratedPrior);
        Assert.Equal(0.75m, configuration.Parameters.SimilarityBand);
        Assert.Equal(12, configuration.ConfigVersion.Length);
    }

    [Fact]
    public void TryBuild_MissingSectionsAndParameters_AreDisabledWithDefaults()
    {
        Assert.True(ExperimentMappingExtensions.TryBuild(new ExperimentsOptions(), out var configuration, out _));

        Assert.False(configuration.Exp1ConsolidationVsQuality.Enabled);
        Assert.False(configuration.Exp2UnratedSuppliers.Enabled);
        Assert.False(configuration.Exp3PartialRouting.Enabled);
        Assert.Equal(RoutingParameters.Default, configuration.Parameters);
    }

    [Fact]
    public void TryBuild_PercentageOver100_IsRejected()
    {
        // AC7
        var error = Assert.Single(Errors(Options(exp2: Experiment(percentage: 150))));

        Assert.Contains("Exp2UnratedSuppliers", error);
        Assert.Contains("between 0 and 100", error);
    }

    [Fact]
    public void TryBuild_FourDigitRange_IsRejected()
    {
        // AC12
        var exp2 = Experiment();
        exp2.Targeting.ZipRanges = ["2100-2199"];

        var error = Assert.Single(Errors(Options(exp2: exp2)));

        Assert.Contains("2100-2199", error);
    }

    [Fact]
    public void TryBuild_BadPrefix_IsRejected()
    {
        var exp1 = Experiment();
        exp1.Targeting.ZipPrefixes = ["021140"];

        Assert.Contains("prefix", Assert.Single(Errors(Options(exp1: exp1))));
    }

    [Fact]
    public void TryBuild_EmptySalt_IsRejected()
        => Assert.Contains("salt", Assert.Single(Errors(Options(exp3: Experiment(salt: "")))));

    [Fact]
    public void TryBuild_MissingSaltOnAPresentSection_IsRejected()
        => Assert.Contains("salt", Assert.Single(Errors(Options(exp3: Experiment(salt: null)))));

    [Fact]
    public void TryBuild_InvalidParameters_AreRejected()
    {
        var exp2 = Experiment();
        exp2.Parameters.UnratedPrior = 11m;
        var options = Options(exp2: exp2);
        options.Routing.SimilarityBand = -1m;

        var error = Assert.Single(Errors(options));

        Assert.Contains("unrated prior", error);
        Assert.Contains("similarity band", error);
    }

    [Fact]
    public void TryBuild_SeveralProblems_AreAllReportedTogether()
    {
        var exp1 = Experiment(percentage: 101);
        var exp2 = Experiment(salt: "");
        exp2.Targeting.ZipRanges = ["2100-2199"];

        var options = Options(exp1: exp1, exp2: exp2);
        options.BindError = "bind failed";

        var errors = Errors(options);

        Assert.Equal(4, errors.Count);
        Assert.Contains(errors, error => error.Contains("bind failed"));
        Assert.Contains(errors, error => error.Contains("Exp1ConsolidationVsQuality") && error.Contains("between 0 and 100"));
        Assert.Contains(errors, error => error.Contains("2100-2199"));
        Assert.Contains(errors, error => error.Contains("Exp2UnratedSuppliers") && error.Contains("salt"));
    }

    [Fact]
    public void TryBuild_Errors_NeverEchoCallerValues()
    {
        var exp2 = Experiment(percentage: 150);
        exp2.Targeting.Callers = ["secret-caller-id"];

        Assert.DoesNotContain("secret-caller-id", string.Join(" ", Errors(Options(exp2: exp2))));
    }

    [Fact]
    public void TryBuild_BlankCaller_IsRejectedWithoutEchoingTheList()
    {
        var exp2 = Experiment();
        exp2.Targeting.Callers = ["secret-caller-id", " "];

        var errors = Errors(Options(exp2: exp2));

        Assert.Contains("must not be blank", Assert.Single(errors));
        Assert.DoesNotContain("secret-caller-id", errors[0]);
    }

    [Fact]
    public void TryBuild_NullOptions_Throws()
        => Assert.Throws<ArgumentNullException>(() => ExperimentMappingExtensions.TryBuild(null!, out _, out _));

    [Fact]
    public void Validator_ReportsTheMapperErrors()
    {
        var validator = new ExperimentsOptionsValidator();

        Assert.True(validator.Validate(null, new ExperimentsOptions()).Succeeded);

        var result = validator.Validate(null, Options(exp2: Experiment(percentage: 150)));

        Assert.True(result.Failed);
        Assert.Contains("between 0 and 100", Assert.Single(result.Failures!));
    }
}
