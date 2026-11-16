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
        foreach (var status in new[] { "201", "400", "401", "500" })
        {
            Assert.True(responses.TryGetProperty(status, out _), $"missing {status}");
        }

        var examples = responses.GetProperty("201").GetProperty("content").GetProperty("application/json").GetProperty("examples");
        Assert.Equal("routed", examples.GetProperty("routed").GetProperty("value").GetProperty("status").GetString());
        Assert.Equal("partially_routed", examples.GetProperty("partially_routed").GetProperty("value").GetProperty("status").GetString());
        Assert.Equal("not_routed", examples.GetProperty("not_routed").GetProperty("value").GetProperty("status").GetString());
        Assert.All(["routed", "partially_routed", "not_routed"], name => Assert.True(examples.GetProperty(name).GetProperty("value").TryGetProperty("id", out _), $"{name} example has no id"));
        var invalid = responses.GetProperty("400").GetProperty("content").GetProperty("application/json").GetProperty("example");
        Assert.Equal(2, invalid.GetProperty("errors").GetArrayLength());
        Assert.NotEmpty(post.GetProperty("security").EnumerateArray());
    }

    // AC9: the 201 documents the Location header on both paths, with the same status codes and examples.
    [Theory]
    [InlineData("/api/v1/route")]
    [InlineData("/api/route")]
    public async Task Document_BothPaths_Document201WithLocationAnd400And401And500(string path)
    {
        var document = await GetDocumentAsync();

        var responses = document.GetProperty("paths").GetProperty(path).GetProperty("post").GetProperty("responses");

        Assert.Equal(["201", "400", "401", "500"], responses.EnumerateObject().Select(response => response.Name).Order());
        Assert.True(responses.GetProperty("201").GetProperty("headers").TryGetProperty("Location", out var location));
        Assert.Contains("/api/v1/route/{id}", location.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Equal(
            ["not_routed", "partially_routed", "routed"],
            responses.GetProperty("201").GetProperty("content").GetProperty("application/json").GetProperty("examples").EnumerateObject().Select(example => example.Name).Order());
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

    // OR-028 AC9: the GET is documented with 200, 401, 404 and 500, an example, and none of the POST-only 400 envelope.
    [Fact]
    public async Task Document_GetStoredOrder_Documents200401404And500WithAnExample()
    {
        var document = await GetDocumentAsync();

        var get = document.GetProperty("paths").GetProperty("/api/v1/route/{id}").GetProperty("get");

        Assert.Equal("GetStoredOrderV1", get.GetProperty("operationId").GetString());
        Assert.Equal(["200", "401", "404", "500"], get.GetProperty("responses").EnumerateObject().Select(response => response.Name).Order());
        Assert.NotEmpty(get.GetProperty("security").EnumerateArray());
        Assert.NotEmpty(get.GetProperty("summary").GetString()!);
        var ok = get.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("example");
        Assert.Equal("routed", ok.GetProperty("status").GetString());
        Assert.True(ok.TryGetProperty("created_at", out _));
        Assert.True(ok.TryGetProperty("items", out _));
        Assert.Equal(404, get.GetProperty("responses").GetProperty("404").GetProperty("content").GetProperty("application/problem+json").GetProperty("example").GetProperty("status").GetInt32());
        Assert.Contains("id", get.GetProperty("parameters").EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()));
    }

    // AC8, AC9: the alias is POST-only.
    [Fact]
    public async Task Document_AliasPath_HasNoGet()
    {
        var document = await GetDocumentAsync();

        var paths = document.GetProperty("paths");

        Assert.False(paths.GetProperty("/api/route").TryGetProperty("get", out _));
        Assert.False(paths.TryGetProperty("/api/route/{id}", out _));
    }
}
