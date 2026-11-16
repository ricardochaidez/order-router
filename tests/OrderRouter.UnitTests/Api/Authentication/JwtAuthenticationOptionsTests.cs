using OrderRouter.Api.Authentication;

namespace OrderRouter.UnitTests.Api.Authentication;

public class JwtAuthenticationOptionsTests
{
    [Fact]
    public void Validate_BothSettingsPresentOutsideDevelopment_DoesNotThrow()
        => new JwtAuthenticationOptions("https://login.example/", "order-router").Validate(isDevelopment: false);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_AuthorityMissingOutsideDevelopment_ThrowsNamingTheSetting(string? authority)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new JwtAuthenticationOptions(authority, "order-router").Validate(isDevelopment: false));

        Assert.Contains("Authentication:Jwt:Authority", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_AudienceMissingOutsideDevelopment_ThrowsNamingTheSetting(string? audience)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new JwtAuthenticationOptions("https://login.example/", audience).Validate(isDevelopment: false));

        Assert.Contains("Authentication:Jwt:Audience", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NothingConfiguredInDevelopment_DoesNotThrow()
        => new JwtAuthenticationOptions().Validate(isDevelopment: true);
}
