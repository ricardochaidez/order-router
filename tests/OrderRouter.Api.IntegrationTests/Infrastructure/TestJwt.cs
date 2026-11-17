using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace OrderRouter.Api.IntegrationTests.Infrastructure;

/// <summary>
/// A throw-away signing key and token builder, so tests never touch a real identity provider. The matching
/// OIDC configuration is injected into the host, so no metadata is fetched over the network.
/// </summary>
internal static class TestJwt
{
    public const string Issuer = "https://test-authority.example/";

    public const string Audience = "order-router-test";

    public const string Subject = "test-caller";

    private static readonly RsaSecurityKey signingKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    private static readonly RsaSecurityKey otherKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    /// <summary>The OIDC configuration the host trusts: the test issuer and the test public key.</summary>
    public static OpenIdConnectConfiguration Configuration()
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(signingKey);

        return configuration;
    }

    /// <summary>A token that passes every validation rule.</summary>
    public static string Valid() => Create();

    /// <summary>A token whose lifetime ended well beyond the clock skew.</summary>
    public static string Expired() => Create(expires: DateTime.UtcNow.AddHours(-1), notBefore: DateTime.UtcNow.AddHours(-2));

    /// <summary>A token signed by a key the host does not trust.</summary>
    public static string BadSignature() => Create(key: otherKey);

    /// <summary>A token issued for a different audience.</summary>
    public static string WrongAudience() => Create(audience: "someone-else");

    /// <summary>A token from a different issuer.</summary>
    public static string WrongIssuer() => Create(issuer: "https://other-authority.example/");

    /// <summary>A valid token whose caller is <paramref name="subject"/> (<c>sub</c>).</summary>
    public static string ForSubject(string subject) => Create(claims: [new System.Security.Claims.Claim("sub", subject)]);

    /// <summary>A valid token carrying exactly these claims and no <c>sub</c> unless one is listed.</summary>
    public static string WithClaims(params (string Type, string Value)[] claims)
        => Create(claims: [.. claims.Select(claim => new System.Security.Claims.Claim(claim.Type, claim.Value))]);

    /// <summary>A valid token with no <c>sub</c>, <c>client_id</c> or <c>azp</c> claim.</summary>
    public static string WithoutCaller() => Create(claims: []);

    private static string Create(
        RsaSecurityKey? key = null,
        string audience = Audience,
        string issuer = Issuer,
        DateTime? expires = null,
        DateTime? notBefore = null,
        IEnumerable<System.Security.Claims.Claim>? claims = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new System.Security.Claims.ClaimsIdentity(claims ?? [new System.Security.Claims.Claim("sub", Subject)]),
            NotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key ?? signingKey, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
