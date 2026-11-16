using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace OrderRouter.Api.Authentication;

/// <summary>
/// Documents the authentication schemes in the OpenAPI document: <c>Bearer</c> always, and <c>ApiKey</c>
/// (<c>X-Api-Key</c>) only when the Development key is enabled. Protected operations get the requirement and a
/// <c>401</c> response, so the routing endpoints are documented once OR-009 adds them.
/// </summary>
internal sealed class SecuritySchemeTransformer(bool devKeyEnabled) : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    private const string bearerName = "Bearer";
    private const string apiKeyName = "ApiKey";

    /// <inheritdoc />
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[bearerName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT bearer token issued by the configured OIDC authority.",
        };

        if (devKeyEnabled)
        {
            document.Components.SecuritySchemes[apiKeyName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = DevApiKeyDefaults.HeaderName,
                Description = "Development-only API key.",
            };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        // Anonymous endpoints (such as /health) stay undocumented as secured.
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(bearerName, context.Document)] = [] });

        if (devKeyEnabled)
        {
            operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(apiKeyName, context.Document)] = [] });
        }

        operation.Responses ??= [];
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid credentials." });

        return Task.CompletedTask;
    }
}
