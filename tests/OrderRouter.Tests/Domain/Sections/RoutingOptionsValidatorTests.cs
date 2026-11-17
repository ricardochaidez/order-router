using OrderRouter.Domain.Sections;

namespace OrderRouter.Tests.Domain.Sections;

public class RoutingOptionsValidatorTests
{
    private readonly RoutingOptionsValidator validator = new();

    [Fact]
    public void Validate_Default_Succeeds()
        => Assert.True(validator.Validate(null, new RoutingOptions()).Succeeded);

    [Fact]
    public void Validate_ValidRange_Succeeds()
        => Assert.True(validator.Validate(null, new RoutingOptions { NationwideZipRange = "00200-99998" }).Succeeded);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("99999-00100")]
    [InlineData("100-999")]
    public void Validate_MalformedOrEmptyRange_FailsNamingTheSetting(string range)
    {
        var result = validator.Validate(null, new RoutingOptions { NationwideZipRange = range });

        Assert.True(result.Failed);
        Assert.Contains("Routing:NationwideZipRange", result.FailureMessage);
    }
}
