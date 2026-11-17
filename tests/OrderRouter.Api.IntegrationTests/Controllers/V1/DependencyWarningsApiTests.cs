using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>
/// OR-020 acceptance on the real host and the seeded database. Every caller is in the EXP-3 treatment arm (a temp
/// <c>experiments.json</c>), and the rules are set per test through <c>ItemDependencies:Rules</c>. ZIP 00098 is served
/// only by the sentinel <c>SUP-T014</c>, which handles wheelchairs, so a walker there is unfulfillable
/// (<c>NO_SUPPLIER_FOR_CATEGORY_IN_AREA</c>) and the wheelchair is routed: a partial route on two known categories.
/// </summary>
public class DependencyWarningsApiTests(ReferenceDataFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string exp3AllTreated = """
        { "Experiments": { "Exp3PartialRouting": { "Enabled": true, "TreatmentPercentage": 100, "Salt": "exp3",
            "Targeting": { "Callers": [], "ZipPrefixes": [], "ZipRanges": [] } } } }
        """;

    private const string wheelchairAndWalker = """
        { "order_id": "ORD-DEP", "customer_zip": "00098", "mail_order": false,
          "items": [ { "product_code": "WC-STD-001", "quantity": 1 }, { "product_code": "WK-STD-009", "quantity": 1 } ] }
        """;

    // ItemDependencies:Rules:{i}:Category and :Requires:{j}, exactly as an environment variable would set them.
    private static Dictionary<string, string> Rule(int index, string category, params string[] requires)
    {
        var settings = new Dictionary<string, string> { [$"ItemDependencies:Rules:{index}:Category"] = category };

        for (var j = 0; j < requires.Length; j++)
        {
            settings[$"ItemDependencies:Rules:{index}:Requires:{j}"] = requires[j];
        }

        return settings;
    }

    private async Task<(JsonElement Post, JsonElement Get)> PostAndGetAsync(IReadOnlyDictionary<string, string> rules)
    {
        var directory = Directory.CreateTempSubdirectory("experiments-");

        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "experiments.json"), exp3AllTreated);
            using var provider = new PhysicalFileProvider(directory.FullName);
            await using var factory = AuthApiFactory.Create(fixture.ConnectionString)
                .WithWebHostBuilder(builder =>
                {
                    foreach (var (key, value) in rules)
                    {
                        builder.UseSetting(key, value);
                    }

                    builder.ConfigureAppConfiguration((_, config) =>
                        config.AddJsonFile(provider, "experiments.json", optional: false, reloadOnChange: false));
                });
            using var client = factory.CreateClient();

            using var post = await client.SendAsync(RouteApiFactory.Post("/api/v1/route", wheelchairAndWalker), Token);
            Assert.Equal(HttpStatusCode.Created, post.StatusCode);
            var created = JsonDocument.Parse(await post.Content.ReadAsStringAsync(Token)).RootElement.Clone();

            using var get = await client.SendAsync(RouteApiFactory.Get(post.Headers.Location!.OriginalString), Token);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var stored = JsonDocument.Parse(await get.Content.ReadAsStringAsync(Token)).RootElement.Clone();

            return (created, stored);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // AC1, and ADR-0007: GET returns exactly the warning the POST returned.
    [Fact]
    public async Task Post_PartialRouteMatchingARule_ReturnsAndStoresTheWarning()
    {
        var (post, get) = await PostAndGetAsync(Rule(0, "wheelchair", "walker"));

        Assert.Equal("partially_routed", post.GetProperty("status").GetString());
        var warning = Assert.Single(post.GetProperty("warnings").EnumerateArray());
        Assert.Equal("DEPENDENT_ITEM_UNFULFILLED", warning.GetProperty("code").GetString());
        Assert.Equal(["WK-STD-009"], warning.GetProperty("unfulfilled_product_codes").EnumerateArray().Select(code => code.GetString()));
        Assert.Equal(["WC-STD-001"], warning.GetProperty("affected_product_codes").EnumerateArray().Select(code => code.GetString()));
        Assert.Equal(
            "WC-STD-001 (wheelchair) is routed, but its required companion WK-STD-009 (walker) could not be fulfilled. Review before releasing.",
            warning.GetProperty("message").GetString());
        Assert.Equal(post.GetProperty("warnings").GetRawText(), get.GetProperty("warnings").GetRawText());
    }

    // AC2: the shipped rule set is empty.
    [Fact]
    public async Task Post_NoRules_HasNoWarnings()
    {
        var (post, _) = await PostAndGetAsync(new Dictionary<string, string>());

        Assert.Equal("partially_routed", post.GetProperty("status").GetString());
        Assert.Empty(post.GetProperty("warnings").EnumerateArray());
    }

    // AC4: the rule names a category the reference data does not have; it is skipped and the request succeeds.
    [Fact]
    public async Task Post_RuleNamingAnUnknownCategory_IsSkipped()
    {
        var (post, _) = await PostAndGetAsync(Rule(0, "wheelchair", "walker", "not a seeded category"));

        Assert.Equal("partially_routed", post.GetProperty("status").GetString());
        Assert.Empty(post.GetProperty("warnings").EnumerateArray());
    }

    // AC6: the routing section is the same with and without rules.
    [Fact]
    public async Task Post_WithAndWithoutRules_RoutesTheSame()
    {
        var (without, _) = await PostAndGetAsync(new Dictionary<string, string>());
        var (with, _) = await PostAndGetAsync(Rule(0, "wheelchair", "walker"));

        Assert.Equal(without.GetProperty("routing").GetRawText(), with.GetProperty("routing").GetRawText());
        Assert.Equal(without.GetProperty("unfulfilled_items").GetRawText(), with.GetProperty("unfulfilled_items").GetRawText());
    }

    // AC3: a malformed rule stops the host, naming the bad key.
    [Fact]
    public async Task Startup_RuleWithEmptyCategory_Fails()
    {
        await using var factory = AuthApiFactory.Create(fixture.ConnectionString)
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ItemDependencies:Rules:0:Category", " ");
                builder.UseSetting("ItemDependencies:Rules:0:Requires:0", "walker");
            });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("ItemDependencies:Rules:0:Category must not be empty.", Flatten(exception), StringComparison.Ordinal);
    }

    // AC5: startup checks only the shape, so a well-formed rule starts on an empty, unmigrated database.
    [Fact]
    public async Task Startup_WellFormedRuleAndEmptyDatabase_Starts()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(Token);
        await using var factory = ApiFactory.Create(database.ConnectionString)
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ItemDependencies:Rules:0:Category", "hospital bed");
                builder.UseSetting("ItemDependencies:Rules:0:Requires:0", "patient lift");
            });

        using var client = factory.CreateClient();

        using var health = await client.GetAsync("/health", Token);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }
}
