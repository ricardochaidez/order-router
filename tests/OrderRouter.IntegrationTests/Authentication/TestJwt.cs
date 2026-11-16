using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace OrderRouter.IntegrationTests.Authentication;

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

    private static string Create(
        RsaSecurityKey? key = null,
        string audience = Audience,
        string issuer = Issuer,
        DateTime? expires = null,
        DateTime? notBefore = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", Subject)]),
            NotBefore = notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key ?? signingKey, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
