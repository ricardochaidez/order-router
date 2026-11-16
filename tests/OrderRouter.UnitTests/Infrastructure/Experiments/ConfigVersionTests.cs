using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Infrastructure.Experiments;
using OrderRouter.Infrastructure.Experiments.Mappers;

namespace OrderRouter.UnitTests.Infrastructure.Experiments;

public class ConfigVersionTests
{
    private static string Version(ExperimentsOptions options)
    {
        Assert.True(ExperimentsOptionsMapper.TryBuild(options, out var configuration, out _));

        return configuration.ConfigVersion;
    }

    private static ExperimentsOptions Sample(int percentage = 20, string salt = "exp2", decimal prior = 6.5m)
    {
        var exp2 = new ExperimentOptions { Enabled = true, TreatmentPercentage = percentage, Salt = salt };
        exp2.Targeting.ZipPrefixes = ["021"];
        exp2.Parameters.UnratedPrior = prior;

        return new ExperimentsOptions { Experiments = new ExperimentsSection { Exp2UnratedSuppliers = exp2 } };
    }

    [Fact]
    public void Compute_SameValues_SameVersion()
        => Assert.Equal(Version(Sample()), Version(Sample()));

    [Fact]
    public void Compute_IsTwelveLowercaseHexCharacters()
    {
        var version = Version(Sample());

        Assert.Equal(12, version.Length);
        Assert.All(version, character => Assert.True(char.IsAsciiHexDigitLower(character)));
    }

    [Fact]
    public void Compute_AnyValueChange_ChangesTheVersion()
    {
        var baseline = Version(Sample());

        Assert.NotEqual(baseline, Version(Sample(percentage: 21)));
        Assert.NotEqual(baseline, Version(Sample(salt: "other")));
        Assert.NotEqual(baseline, Version(Sample(prior: 7m)));

        var withCaller = Sample();
        withCaller.Experiments.Exp2UnratedSuppliers!.Targeting.Callers = ["c"];
        Assert.NotEqual(baseline, Version(withCaller));

        var withBand = Sample();
        withBand.Routing.SimilarityBand = 0.25m;
        Assert.NotEqual(baseline, Version(withBand));
    }

    [Fact]
    public void Compute_TrailingZerosInANumber_DoNotChangeTheVersion()
        => Assert.Equal(Version(Sample(prior: 6.5m)), Version(Sample(prior: 6.50m)));

    [Fact]
    public void Compute_ExplicitDefaultsEqualMissingValues()
    {
        var explicitDefaults = Sample(prior: RoutingParameters.DefaultUnratedPrior);
        explicitDefaults.Routing.SimilarityBand = RoutingParameters.Default.SimilarityBand;
        explicitDefaults.Experiments.Exp1ConsolidationVsQuality = null;

        Assert.Equal(Version(Sample()), Version(explicitDefaults));
    }

    [Fact]
    public void Compute_RangeIsPartOfTheVersion()
    {
        var withRange = Sample();
        withRange.Experiments.Exp2UnratedSuppliers!.Targeting.ZipRanges = ["30000-30999"];

        Assert.NotEqual(Version(Sample()), Version(withRange));
    }
}
