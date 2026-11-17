using OrderRouter.Domain.Entities.Experiments;
using OrderRouter.Domain.Models;

namespace OrderRouter.Tests.Domain.Entities.Experiments;

public class RoutingParametersTests
{
    [Fact]
    public void Default_HasDocumentedValues()
    {
        Assert.Equal(0.5m, RoutingParameters.Default.SimilarityBand);
        Assert.Equal(2.0m, RoutingParameters.Default.QualityUpliftThreshold);
        Assert.Equal(6.5m, RoutingParameters.Default.UnratedPrior);
    }

    [Theory]
    [InlineData(0, 0.01, 1)]
    [InlineData(0.5, 2.0, 10)]
    public void Constructor_BoundaryValues_AreAccepted(double band, double uplift, double prior)
    {
        var parameters = new RoutingParameters((decimal)band, (decimal)uplift, (decimal)prior);

        Assert.Equal((decimal)prior, parameters.UnratedPrior);
    }

    [Theory]
    [InlineData(-0.01, 2.0, 6.5, "similarity band")]
    [InlineData(0.5, 0, 6.5, "uplift")]
    [InlineData(0.5, 2.0, 0.99, "unrated prior")]
    [InlineData(0.5, 2.0, 10.01, "unrated prior")]
    public void Constructor_OutOfRange_Throws(double band, double uplift, double prior, string expected)
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => new RoutingParameters((decimal)band, (decimal)uplift, (decimal)prior));

        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void Constructor_SeveralProblems_ReportsAllAtOnce()
    {
        var exception = Assert.Throws<DomainValidationException>(() => new RoutingParameters(-1m, 0m, 0m));

        Assert.Contains("similarity band", exception.Message);
        Assert.Contains("uplift", exception.Message);
        Assert.Contains("unrated prior", exception.Message);
    }
}
