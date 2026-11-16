using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace OrderRouter.Api.Authentication;

/// <summary>
/// Authenticates the development key sent in <c>X-Api-Key</c> (FR-19). The scheme is only registered in the
/// Development environment, so production cannot enable it by configuration (SC-14).
/// </summary>
internal sealed class DevApiKeyAuthenticationHandler(
    IOptionsMonitor<DevApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<DevApiKeyAuthenticationOptions>(options, logger, encoder)
{
    // A fixed message: a failure must never echo the sent or the expected key (NFR-11).
    private const string failureMessage = "The API key is not valid.";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(DevApiKeyDefaults.HeaderName, out var values))
        {
            // No header: let the other scheme (JWT bearer) decide.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!IsValid(values.ToString(), Options.ApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail(failureMessage));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, DevApiKeyDefaults.CallerId),
                new Claim("sub", DevApiKeyDefaults.CallerId),
            ],
            Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Compares in constant time. Hashing both values first means the comparison cost does not depend on where
    /// they differ, and a different length does not leak.
    /// </summary>
    private static bool IsValid(string sent, string expected)
    {
        if (sent.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var sentHash = SHA256.HashData(Encoding.UTF8.GetBytes(sent));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));

        return CryptographicOperations.FixedTimeEquals(sentHash, expectedHash);
    }
}
