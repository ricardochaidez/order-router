using Microsoft.Extensions.Options;
using OrderRouter.Domain.Sections;
using OrderRouter.Infrastructure.Services;

namespace OrderRouter.Tests.Infrastructure.Services;

public class CallerHashingTests
{
    private static HmacCallerHasher Create(string secret = "unit-test-secret")
        => new(Options.Create(new CallerHashingOptions { Secret = secret }));

    [Fact]
    public void Hash_SameCallerTwice_ReturnsTheSameHash()
    {
        var hasher = Create();

        Assert.Equal(hasher.Hash("user-123"), hasher.Hash("user-123"));
    }

    [Fact]
    public void Hash_DifferentCallers_ReturnDifferentHashes()
    {
        var hasher = Create();

        Assert.NotEqual(hasher.Hash("user-123"), hasher.Hash("user-124"));
    }

    [Fact]
    public void Hash_DifferentSecrets_ReturnDifferentHashes()
    {
        Assert.NotEqual(Create("unit-test-secret").Hash("user-123"), Create("other-secret").Hash("user-123"));
    }

    // Known answers computed outside .NET (Python hmac), so a change of algorithm or encoding is caught.
    [Theory]
    [InlineData("unit-test-secret", "5eef3b6419065dc11a09ab5521ba24b060554809477f94769036653071e06934")]
    [InlineData("other-secret", "7e1b374f0dba5b209843fe04d16c5f6abfe0c7ec0164bcb6b623ada964881a8b")]
    public void Hash_KnownCallerAndSecret_ReturnsTheKnownHmacSha256(string secret, string expected)
    {
        Assert.Equal(expected, Create(secret).Hash("user-123"));
    }

    // AC8 at unit level: the pseudonym must not reveal the caller.
    [Fact]
    public void Hash_Always_DoesNotContainTheRawCallerId()
    {
        var hash = Create().Hash("user-123");

        Assert.DoesNotContain("user-123", hash, StringComparison.Ordinal);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_BlankCaller_Throws(string? callerId)
    {
        Assert.ThrowsAny<ArgumentException>(() => Create().Hash(callerId!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_MissingSecret_Throws(string? secret)
    {
        Assert.Throws<InvalidOperationException>(() => new HmacCallerHasher(Options.Create(new CallerHashingOptions { Secret = secret })));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_MissingSecret_FailsNamingTheSettingAndNeverAValue(string? secret)
    {
        var result = new CallerHashingOptionsValidator().Validate(null, new CallerHashingOptions { Secret = secret });

        Assert.True(result.Failed);
        Assert.Contains("CallerHashing:Secret", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_SecretPresent_Succeeds()
    {
        var result = new CallerHashingOptionsValidator().Validate(null, new CallerHashingOptions { Secret = "x" });

        Assert.True(result.Succeeded);
    }
}
