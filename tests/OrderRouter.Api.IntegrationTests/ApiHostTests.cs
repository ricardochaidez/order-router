using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OrderRouter.Api.IntegrationTests;

public class ApiHostTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    // Proves the host composes and boots, without exercising any business logic.
    [Fact]
    public async Task GetOpenApiDocument_InDevelopment_ReturnsOk()
    {
        using var client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                // The host refuses to start without the caller-hash secret (OR-027).
                builder.UseSetting("CallerHashing:Secret", TestCallerHashing.Secret);
            })
            .CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
