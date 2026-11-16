using OrderRouter.Domain.Common;
using OrderRouter.Domain.Routing.Experiments;

namespace OrderRouter.UnitTests.Domain.Routing.Experiments;

public class ExperimentDefinitionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Constructor_PercentageAtTheEdges_IsValid(int percentage)
        => Assert.Equal(percentage, new ExperimentDefinition("e", true, percentage, "s", ExperimentTargeting.Everyone).TreatmentPercentage);

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Constructor_PercentageOutOfRange_Throws(int percentage)
    {
        var exception = Assert.Throws<DomainValidationException>(() => new ExperimentDefinition("e", true, percentage, "s", ExperimentTargeting.Everyone));

        Assert.Contains("between 0 and 100", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Constructor_EmptySalt_Throws(string? salt)
        => Assert.Throws<DomainValidationException>(() => new ExperimentDefinition("e", true, 10, salt!, ExperimentTargeting.Everyone));

    [Fact]
    public void Constructor_SeveralErrors_AreReportedTogether()
    {
        var exception = Assert.Throws<DomainValidationException>(() => new ExperimentDefinition(" ", true, 101, "", ExperimentTargeting.Everyone));

        Assert.Contains("experiment id", exception.Message);
        Assert.Contains("between 0 and 100", exception.Message);
        Assert.Contains("salt", exception.Message);
    }

    [Fact]
    public void Constructor_NullTargeting_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ExperimentDefinition("e", true, 10, "s", null!));

    [Fact]
    public void Disabled_IsDisabledAtZeroPercent()
    {
        var definition = ExperimentDefinition.Disabled("e", "salt");

        Assert.False(definition.Enabled);
        Assert.Equal(0, definition.TreatmentPercentage);
        Assert.Equal("salt", definition.Salt);
    }
}
