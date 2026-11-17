using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using OrderRouter.Api.Extensions;
using OrderRouter.Api.Filters;
using OrderRouter.Api.OpenApi;
using Scalar.AspNetCore;

namespace OrderRouter.Api;

/// <summary>
/// The thin composition of the host: <see cref="ConfigureServices"/> registers everything through the extension
/// methods in <c>Extensions/</c>, and <see cref="Configure"/> builds the middleware pipeline.
/// </summary>
/// <param name="configuration">The application configuration.</param>
/// <param name="environment">The hosting environment.</param>
public sealed class Startup(IConfiguration configuration, IWebHostEnvironment environment)
{
    /// <summary>Registers the services.</summary>
    /// <param name="services">The service collection.</param>
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // OpenAPI document generation. Every endpoint is documented through it.
        services.AddOpenApi(options => options.AddOperationTransformer<RouteOpenApiTransformer>());

        // JWT bearer in production, plus the X-Api-Key dev key in Development only. The RouteCaller policy is also the
        // fallback, so every endpoint needs authentication unless it opts out with AllowAnonymous (FR-19).
        services.AddOrderRouterAuthentication(configuration, environment);

        // [ApiController] validates the data annotations on the request view models before an action runs; for actions
        // marked [RouteEnvelope] the automatic 400 is written in the v1 envelope (SC-15) instead of
        // ValidationProblemDetails. Unknown request fields stay ignored (tolerant reader, DQ-15) because
        // UnmappedMemberHandling is left at Skip.
        services.AddControllers()
            .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(RouteEnvelopeInvalidModelStateResponse.Configure);

        // The same JSON settings for everything written outside MVC: exception-handler ProblemDetails, and the OpenAPI
        // schemas, which Microsoft.AspNetCore.OpenApi always derives from these options.
        services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

        // Unexpected faults become 500 ProblemDetails with no internals (CLAUDE.md). MVC's ProblemDetailsFactory (controller
        // 404s) adds "traceId" verbatim, while the host writer (500s) names it with the API's policy ("trace_id"). Renaming it
        // here gives every ProblemDetails the same shape under the one JSON naming policy.
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var extensions = context.ProblemDetails.Extensions;

            if (extensions.Remove("traceId", out var traceId))
            {
                extensions[JsonNamingPolicy.SnakeCaseLower.ConvertName("traceId")] = traceId;
            }
        });

        services.ConfigureDbContext(configuration);
        services.ConfigureServices(configuration);
        services.ConfigureHealthChecks();
    }

    /// <summary>Builds the middleware pipeline and maps the endpoints.</summary>
    /// <param name="app">The built application.</param>
    public void Configure(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // The OpenAPI document and the Scalar UI (/scalar) are only exposed in Development.
        if (environment.IsDevelopment())
        {
            // Documentation only, and only mapped in Development, so it stays anonymous.
            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference().AllowAnonymous();
        }

        // Unexpected faults become 500 ProblemDetails with no internals (CLAUDE.md).
        app.UseExceptionHandler();

        app.UseHttpsRedirection();

        // Authentication runs before anything else touches the request: no credentials, no validation, no routing.
        app.UseAuthentication();
        app.UseAuthorization();

        // Attribute-routed controllers: RouteController and HealthController.
        app.MapControllers();
    }

    // One JSON naming policy for the whole API: snake_case properties and snake_case enum strings (FR-01, ADR-0004).
    // MVC and the rest of the host keep separate option instances, so both are configured from here to stay identical.
    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    }
}
