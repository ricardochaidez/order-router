using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using OrderRouter.Api.Authentication;
using OrderRouter.Api.Health;
using OrderRouter.Api.Routing;
using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.PlanSearch;
using OrderRouter.Infrastructure.CallerHashing;
using OrderRouter.Infrastructure.Experiments;
using OrderRouter.Infrastructure.Persistence;
using OrderRouter.Infrastructure.Randomness;

// Composition root: the Api project is the only place that wires Application and Infrastructure
// together, so the inner layers never depend on the host or on each other's implementations.
var builder = WebApplication.CreateBuilder(args);

// Reloadable experiment settings (SC-25, ADR-0005). The file is optional: when it is missing every experiment is
// disabled, which is the fail-safe default (AB-03). It is placed just before the environment-variable source, so
// environment variables and the command line still override it. Containers poll for changes
// (DOTNET_USE_POLLING_FILE_WATCHER), because file events do not reliably cross bind mounts.
builder.Configuration.AddJsonFile("experiments.json", optional: true, reloadOnChange: true);
var configurationSources = builder.Configuration.Sources;
var experimentsSource = configurationSources[^1];
configurationSources.RemoveAt(configurationSources.Count - 1);
var environmentIndex = configurationSources.ToList().FindIndex(source => source is EnvironmentVariablesConfigurationSource);
configurationSources.Insert(environmentIndex < 0 ? configurationSources.Count : environmentIndex, experimentsSource);

// OpenAPI document generation. Every endpoint added by later tickets is documented through it.
builder.Services.AddOpenApi(options => options.AddOperationTransformer<RouteOpenApiTransformer>());

// JWT bearer in production, plus the X-Api-Key dev key in Development only. The RouteCaller policy is also the
// fallback, so every endpoint needs authentication unless it opts out with AllowAnonymous (FR-19).
builder.AddOrderRouterAuthentication();

// Controllers (Controllers/). [ApiController] validates the data annotations on the request DTOs before an action
// runs; for actions marked [RouteEnvelope] the automatic 400 is written in the v1 envelope (SC-15) instead of
// ValidationProblemDetails. Unknown request fields stay ignored (tolerant reader, DQ-15) because
// UnmappedMemberHandling is left at Skip.
builder.Services.AddControllers()
    .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(RouteEnvelopeInvalidModelStateResponse.Configure);

// The same JSON settings for everything written outside MVC: exception-handler ProblemDetails, and the OpenAPI
// schemas, which Microsoft.AspNetCore.OpenApi always derives from these options.
builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

// Unexpected faults become 500 ProblemDetails with no internals (CLAUDE.md). MVC's ProblemDetailsFactory (controller
// 404s) adds "traceId" verbatim, while the host writer (500s) names it with the API's policy ("trace_id"). Renaming it
// here gives every ProblemDetails the same shape under the one JSON naming policy.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var extensions = context.ProblemDetails.Extensions;

    if (extensions.Remove("traceId", out var traceId))
    {
        extensions[JsonNamingPolicy.SnakeCaseLower.ConvertName("traceId")] = traceId;
    }
});

// Routing settings (FR-09): a bad nationwide ZIP range stops the host at startup.
builder.Services.AddOptions<RoutingOptions>()
    .Bind(builder.Configuration.GetSection(RoutingOptions.SectionName))
    .ValidateOnStart();

// Plan-search guard rails from the PlanSearch section (NFR-05). Invalid limits stop the host at startup.
builder.Services.AddOptions<PlanSearchOptions>()
    .Bind(builder.Configuration.GetSection(PlanSearchOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<PlanSearchOptions>, PlanSearchOptionsValidator>();
// The routing use case, domain service, policies and plan search. The Domain types are wired in the Application
// layer, so this host only references Application and Infrastructure.
builder.Services.AddOrderRouterRouting();
builder.Services.AddOrderRouterRandomness();
// The experiment assigner (OR-016): the port is in Application, the reloadable-configuration adapter in Infrastructure.
builder.Services.AddOrderRouterExperiments();
// The caller hasher (OR-027): HMAC-SHA256 with CallerHashing:Secret. The secret comes from the environment or
// user-secrets, never from a committed file; a missing secret stops the host at startup.
builder.Services.AddOrderRouterCallerHashing(builder.Configuration);

// ConnectionStrings:OrderRouter comes from appsettings.Development.json in Development, or from the
// ConnectionStrings__OrderRouter environment variable. There is deliberately no default elsewhere, so a
// mis-configured deployment fails at startup instead of silently using a dev password.
var connectionString = builder.Configuration.GetConnectionString("OrderRouter");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("The connection string 'ConnectionStrings:OrderRouter' is not configured.");
}

// The API never migrates or seeds; that is an explicit, separate step (SC-22, SC-33).
builder.Services.AddOrderRouterDatabase(connectionString);
builder.Services.AddHealthChecks().AddOrderRouterDatabaseHealthCheck();

// Exit at startup if PostgreSQL is unreachable, using the same check as GET /health. Connectivity only: an
// empty or unmigrated database starts normally (SC-23, SC-33).
builder.Services.AddHostedService<DependencyStartupCheck>();

var app = builder.Build();

// The OpenAPI document and the Scalar UI (/scalar) are only exposed in Development.
if (app.Environment.IsDevelopment())
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

app.Run();

// One JSON naming policy for the whole API: snake_case properties and snake_case enum strings (FR-01, ADR-0004).
// MVC and the rest of the host keep separate option instances, so both are configured from here to stay identical.
static void ConfigureJson(JsonSerializerOptions options)
{
    options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
}
