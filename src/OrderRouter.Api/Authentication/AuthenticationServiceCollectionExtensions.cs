using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.IdentityModel.Tokens;

namespace OrderRouter.Api.Authentication;

/// <summary>Registers authentication and authorization (FR-19).</summary>
internal static class AuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Adds JWT bearer (via the OIDC authority), the Development-only <c>X-Api-Key</c> scheme, and the single
    /// <see cref="AuthorizationPolicyNames.RouteCaller"/> policy. The same policy is the fallback, so every
    /// endpoint needs authentication unless it is explicitly <c>AllowAnonymous</c> (secure by default).
    /// </summary>
    /// <exception cref="InvalidOperationException">JWT settings are missing outside Development.</exception>
    public static WebApplicationBuilder AddOrderRouterAuthentication(this WebApplicationBuilder builder)
    {
        var isDevelopment = builder.Environment.IsDevelopment();
        var jwt = builder.Configuration.GetSection(JwtAuthenticationOptions.SectionName).Get<JwtAuthenticationOptions>() ?? new JwtAuthenticationOptions();
        jwt.Validate(isDevelopment);

        var devApiKey = builder.Configuration[DevApiKeyDefaults.ConfigurationKey];
        var devKeyEnabled = isDevelopment && !string.IsNullOrWhiteSpace(devApiKey);

        // Microsoft.AspNetCore.Authentication.JwtBearer validates the bearer tokens of production callers.
        var authentication = builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = jwt.Authority;
                options.Audience = jwt.Audience;
                options.RequireHttpsMetadata = !isDevelopment;

                // Keep the token's own claim names, so `sub` stays `sub` (the caller ID, ADR-0005).
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                };
            });

        var schemes = new List<string> { JwtBearerDefaults.AuthenticationScheme };

        // The scheme does not exist outside Development, so no configuration can turn it on (SC-14).
        if (devKeyEnabled)
        {
            authentication.AddScheme<DevApiKeyAuthenticationOptions, DevApiKeyAuthenticationHandler>(
                DevApiKeyDefaults.Scheme,
                options => options.ApiKey = devApiKey!);
            schemes.Add(DevApiKeyDefaults.Scheme);
        }
        else if (!string.IsNullOrWhiteSpace(devApiKey))
        {
            builder.Services.AddHostedService<DevApiKeyIgnoredNotice>();
        }

        builder.Services.AddOpenApi(options =>
        {
            var transformer = new SecuritySchemeTransformer(devKeyEnabled);
            options.AddDocumentTransformer(transformer);
            options.AddOperationTransformer(transformer);
        });

        var policy = new AuthorizationPolicyBuilder(schemes.ToArray())
            .RequireAuthenticatedUser()
            .Build();

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicyNames.RouteCaller, policy);
            options.FallbackPolicy = policy;
        });

        return builder;
    }
}
