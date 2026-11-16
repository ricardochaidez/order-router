using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.FileProviders;
using OrderRouter.IntegrationTests.Authentication;

namespace OrderRouter.IntegrationTests.Routing;

/// <summary>
/// OR-022 acceptance: the optional <c>diagnostics</c> object is off by default and shown on POST, the alias and GET
/// only when <c>Routing:ExposeDiagnostics</c> is on (AB-05, OQ-08). Every test uses its own <c>order_id</c>, because
/// the shared database accumulates stored orders.
/// </summary>
public class DiagnosticsApiTests(ReferenceDataFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly string alice = TestJwt.ForSubject("alice");

    private static readonly string[] experimentNames = ["exp1_consolidation_vs_quality", "exp2_unrated_suppliers", "exp3_partial_routing"];

    private static string NewOrderId() => $"ORD-{Guid.NewGuid():N}";

    private static string Body(string orderId, string code = "WC-STD-001")
        => $$"""
            { "order_id": "{{orderId}}", "customer_zip": "10015", "mail_order": false,
              "items": [ { "product_code": "{{code}}", "quantity": 1 } ] }
            """;

    private WebApplicationFactory<Program> Host(bool expose, IFileProvider? experiments = null)
        => RouteApiFactory.Create(fixture.ConnectionString, useCase: null, exposeDiagnostics: expose, experimentsFile: experiments);

    private static async Task<(HttpResponseMessage Response, JsonElement Json)> SendAsync(WebApplicationFactory<Program> host, HttpRequestMessage request)
    {
        using var client = host.CreateClient();
        var response = await client.SendAsync(request, Token);
        var text = await response.Content.ReadAsStringAsync(Token);

        return (response, text.Length > 0 && text[0] == '{' ? JsonDocument.Parse(text).RootElement.Clone() : default);
    }

    private static Task<(HttpResponseMessage Response, JsonElement Json)> PostAsync(WebApplicationFactory<Program> host, string body, string path = "/api/v1/route")
        => SendAsync(host, RouteApiFactory.Post(path, body, token: alice));

    private static Task<(HttpResponseMessage Response, JsonElement Json)> GetAsync(WebApplicationFactory<Program> host, string id)
        => SendAsync(host, RouteApiFactory.Get($"/api/v1/route/{id}", token: alice));

    // AC1
    [Fact]
    public async Task Post_DefaultConfiguration_HasNoDiagnosticsProperty()
    {
        await using var host = RouteApiFactory.Create(fixture.ConnectionString, useCase: null);

        var (response, json) = await PostAsync(host, Body(NewOrderId()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(json.TryGetProperty("diagnostics", out _));
    }

    // AC2
    [Fact]
    public async Task Post_FlagOn_ShowsTheThreeExperimentsAndTheExactSearchMode()
    {
        await using var host = Host(expose: true);

        var (response, json) = await PostAsync(host, Body(NewOrderId()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var diagnostics = json.GetProperty("diagnostics");
        var arms = diagnostics.GetProperty("experiment_arms");
        Assert.Equal(experimentNames, arms.EnumerateObject().Select(property => property.Name));
        Assert.All(arms.EnumerateObject(), property =>
        {
            Assert.Equal("control", property.Value.GetProperty("arm").GetString());
            Assert.Equal("disabled", property.Value.GetProperty("reason").GetString());
        });
        Assert.Equal("exact", diagnostics.GetProperty("search_mode").GetString());
        Assert.Equal(["experiment_arms", "search_mode"], diagnostics.EnumerateObject().Select(property => property.Name));
    }

    // AC2: an experiment on treatment shows its arm and reason.
    [Fact]
    public async Task Post_FlagOnAndExp2OnTreatment_ShowsTreatmentBucketed()
    {
        using var directory = new TempDirectory();
        directory.Write("experiments.json", """
            { "Experiments": { "Exp2UnratedSuppliers": { "Enabled": true, "TreatmentPercentage": 100, "Salt": "exp2",
              "Targeting": { "Callers": [], "ZipPrefixes": [], "ZipRanges": [] } } } }
            """);
        await using var host = Host(expose: true, directory.Provider);

        var (_, json) = await PostAsync(host, Body(NewOrderId()));

        var arms = json.GetProperty("diagnostics").GetProperty("experiment_arms");
        Assert.Equal("treatment", arms.GetProperty("exp2_unrated_suppliers").GetProperty("arm").GetString());
        Assert.Equal("bucketed", arms.GetProperty("exp2_unrated_suppliers").GetProperty("reason").GetString());
        Assert.Equal("control", arms.GetProperty("exp1_consolidation_vs_quality").GetProperty("arm").GetString());
    }

    // Decided with the engineer: no search ran, so search_mode is omitted and the arms stay.
    [Fact]
    public async Task Post_FlagOnAndOnlyUnknownProducts_HasArmsWithoutSearchMode()
    {
        await using var host = Host(expose: true);

        var (response, json) = await PostAsync(host, Body(NewOrderId(), "NOPE-000"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var diagnostics = json.GetProperty("diagnostics");
        Assert.Equal(3, diagnostics.GetProperty("experiment_arms").EnumerateObject().Count());
        Assert.False(diagnostics.TryGetProperty("search_mode", out _));
    }

    // AC3: one stored order, read with the flag off and then on.
    [Fact]
    public async Task Get_SameStoredOrder_FlagOffHasNoDiagnosticsAndFlagOnEqualsPost()
    {
        await using var on = Host(expose: true);
        await using var off = Host(expose: false);
        var (_, post) = await PostAsync(on, Body(NewOrderId()));
        var id = post.GetProperty("id").GetString()!;

        var (offResponse, offBody) = await GetAsync(off, id);
        var (onResponse, onBody) = await GetAsync(on, id);

        Assert.Equal(HttpStatusCode.OK, offResponse.StatusCode);
        Assert.False(offBody.TryGetProperty("diagnostics", out _));
        Assert.Equal(HttpStatusCode.OK, onResponse.StatusCode);
        Assert.Equal(post.GetProperty("diagnostics").GetRawText(), onBody.GetProperty("diagnostics").GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_Alias_FollowsTheFlag(bool expose)
    {
        await using var host = Host(expose);

        var (response, json) = await PostAsync(host, Body(NewOrderId()), "/api/route");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expose, json.TryGetProperty("diagnostics", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_InvalidBody_NeverHasDiagnostics(bool expose)
    {
        await using var host = Host(expose);

        var (response, json) = await PostAsync(host, """{ "order_id": "X", "items": [] }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(json.TryGetProperty("diagnostics", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_UnknownId_Is404WithoutDiagnostics(bool expose)
    {
        await using var host = Host(expose);

        var (response, json) = await GetAsync(host, Guid.CreateVersion7().ToString());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(json.ValueKind == JsonValueKind.Object && json.TryGetProperty("diagnostics", out _));
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly string path = Directory.CreateTempSubdirectory("diagnostics-").FullName;

        public IFileProvider Provider => new PhysicalFileProvider(path);

        public void Write(string name, string content) => File.WriteAllText(Path.Combine(path, name), content);

        public void Dispose() => Directory.Delete(path, recursive: true);
    }
}
