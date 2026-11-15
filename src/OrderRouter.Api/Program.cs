// Composition root: the Api project is the only place that wires Application and Infrastructure
// together, so the inner layers never depend on the host or on each other's implementations.
var builder = WebApplication.CreateBuilder(args);

// OpenAPI document generation. Every endpoint added by later tickets is documented through it.
builder.Services.AddOpenApi();

var app = builder.Build();

// The OpenAPI document is only exposed in Development.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
