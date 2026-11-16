using System.Text.Json;

namespace OrderRouter.IntegrationTests.Routing;

/// <summary>AC10: the <c>v1</c> OpenAPI document describes both route paths with examples.</summary>
public class RouteOpenApiTests(ReferenceDataFixture fixture)
{
    private async Task<JsonElement> GetDocumentAsync()
    {
        await using var factory = RouteApiFactory.Create(fixture.ConnectionString, new RecordingRouteUseCase());
        using var client = factory.CreateClient();

        var text = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task Document_CanonicalPath_HasExamplesAndAllStatusCodes()
    {
        var document = await GetDocumentAsync();

        var post = document.GetProperty("paths").GetProperty("/api/v1/route").GetProperty("post");
        var request = post.GetProperty("requestBody").GetProperty("content").GetProperty("application/json");
        Assert.Equal("ORD-EXAMPLE", request.GetProperty("example").GetProperty("order_id").GetString());

        var responses = post.GetProperty("responses");
        foreach (var status in new[] { "200", "400", "401", "500" })
        {
            Assert.True(responses.TryGetProperty(status, out _), $"missing {status}");
        }

        var examples = responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("examples");
        Assert.Equal("routed", examples.GetProperty("routed").GetProperty("value").GetProperty("status").GetString());
        Assert.Equal("not_routed", examples.GetProperty("not_routed").GetProperty("value").GetProperty("status").GetString());
        var invalid = responses.GetProperty("400").GetProperty("content").GetProperty("application/json").GetProperty("example");
        Assert.Equal(2, invalid.GetProperty("errors").GetArrayLength());
        Assert.NotEmpty(post.GetProperty("security").EnumerateArray());
    }

    [Fact]
    public async Task Document_AliasPath_IsListedAndDescribedAsPinnedAlias()
    {
        var document = await GetDocumentAsync();

        var post = document.GetProperty("paths").GetProperty("/api/route").GetProperty("post");

        var description = post.GetProperty("description").GetString()!;
        Assert.Contains("Alias of `POST /api/v1/route`", description, StringComparison.Ordinal);
        Assert.Contains("pinned to v1", description, StringComparison.Ordinal);
        Assert.True(post.GetProperty("responses").TryGetProperty("400", out _));
    }

    [Fact]
    public async Task Document_Schemas_UseSnakeCaseNames()
    {
        var document = await GetDocumentAsync();

        var schemas = document.GetProperty("components").GetProperty("schemas");
        var response = schemas.GetProperty("RouteResponse").GetProperty("properties");
        foreach (var name in new[] { "feasible", "status", "routing", "unfulfilled_items", "warnings", "errors" })
        {
            Assert.True(response.TryGetProperty(name, out _), $"missing {name}");
        }

        var request = schemas.GetProperty("RouteRequest").GetProperty("properties");
        Assert.True(request.TryGetProperty("customer_zip", out _));
        Assert.True(request.TryGetProperty("mail_order", out _));
    }
}
