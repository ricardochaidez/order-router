using OrderRouter.Api;
using OrderRouter.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Reloadable experiment settings (SC-25, ADR-0005). The file is optional: when it is missing every experiment is
// disabled, which is the fail-safe default (AB-03). It overrides appsettings*.json and user-secrets; environment
// variables and the command line still override it. Containers poll for changes (DOTNET_USE_POLLING_FILE_WATCHER),
// because file events do not reliably cross bind mounts.
builder.Configuration.AddExperimentsFile();

// Startup keeps the registrations and the pipeline; Program only builds and runs the host, which is also what the
// integration tests' WebApplicationFactory<Program> starts.
var startup = new Startup(builder.Configuration, builder.Environment);
startup.ConfigureServices(builder.Services);

var app = builder.Build();
startup.Configure(app);

app.Run();
