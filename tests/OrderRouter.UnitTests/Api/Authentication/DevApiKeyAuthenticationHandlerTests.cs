using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderRouter.Api.Authentication;

namespace OrderRouter.UnitTests.Api.Authentication;

public class DevApiKeyAuthenticationHandlerTests
{
    private const string defaultKey = "configured-dev-key";

    private static async Task<AuthenticateResult> AuthenticateAsync(string? header, string configuredKey = defaultKey)
    {
        var options = new DevApiKeyAuthenticationOptions { ApiKey = configuredKey };
        var handler = new DevApiKeyAuthenticationHandler(new StaticOptionsMonitor(options), NullLoggerFactory.Instance, UrlEncoder.Default);
        var context = new DefaultHttpContext();

        if (header is not null)
        {
            context.Request.Headers[DevApiKeyDefaults.HeaderName] = header;
        }

        await handler.InitializeAsync(new AuthenticationScheme(DevApiKeyDefaults.Scheme, null, typeof(DevApiKeyAuthenticationHandler)), context);

        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task Authenticate_NoHeader_ReturnsNoResult()
    {
        var result = await AuthenticateAsync(null);

        Assert.True(result.None);
    }

    [Fact]
    public async Task Authenticate_EmptyHeader_Fails()
    {
        var result = await AuthenticateAsync(string.Empty);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Theory]
    [InlineData("wrong-dev-key-same-len")]
    [InlineData("x")]
    [InlineData("configured-dev-key-and-more")]
    public async Task Authenticate_WrongKey_FailsWithoutEchoingEitherKey(string sent)
    {
        var result = await AuthenticateAsync(sent);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(sent, result.Failure!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(defaultKey, result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticate_ConfiguredKeyEmpty_FailsEvenForEmptyHeader()
    {
        var result = await AuthenticateAsync(string.Empty, configuredKey: string.Empty);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Authenticate_CorrectKey_SucceedsAsDevKeyCaller()
    {
        var result = await AuthenticateAsync(defaultKey);

        Assert.True(result.Succeeded);
        Assert.Equal(DevApiKeyDefaults.CallerId, result.Principal!.FindFirst("sub")?.Value);
        Assert.True(result.Principal.Identity!.IsAuthenticated);
    }

    private sealed class StaticOptionsMonitor(DevApiKeyAuthenticationOptions value) : IOptionsMonitor<DevApiKeyAuthenticationOptions>
    {
        public DevApiKeyAuthenticationOptions CurrentValue => value;

        public DevApiKeyAuthenticationOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<DevApiKeyAuthenticationOptions, string?> listener) => null;
    }
}
