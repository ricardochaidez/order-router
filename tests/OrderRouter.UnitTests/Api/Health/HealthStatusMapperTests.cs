using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderRouter.Api.Health;

namespace OrderRouter.UnitTests.Api.Health;

public class HealthStatusMapperTests
{
    [Fact]
    public void Map_Healthy_Returns200AndHealthy()
    {
        var (statusCode, body) = HealthStatusMapper.Map(HealthStatus.Healthy);

        Assert.Equal(200, statusCode);
        Assert.Equal("Healthy", body);
    }

    [Theory]
    [InlineData(HealthStatus.Degraded)]
    [InlineData(HealthStatus.Unhealthy)]
    public void Map_NotHealthy_Returns503AndUnhealthy(HealthStatus status)
    {
        var (statusCode, body) = HealthStatusMapper.Map(status);

        Assert.Equal(503, statusCode);
        Assert.Equal("Unhealthy", body);
    }

    [Fact]
    public void Map_UnknownStatusValue_FailsSafeTo503()
    {
        var (statusCode, body) = HealthStatusMapper.Map((HealthStatus)42);

        Assert.Equal(503, statusCode);
        Assert.Equal("Unhealthy", body);
    }
}
