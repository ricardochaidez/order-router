using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Storage;

namespace OrderRouter.UnitTests.Application.Routing;

public class DiagnosticsTests
{
    // The same policy the API configures (FR-01, ADR-0004).
    private static readonly JsonSerializerOptions wireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private const string armsJson = """
        { "exp1_consolidation_vs_quality": { "arm": "treatment", "reason": "bucketed" },
          "exp2_unrated_suppliers": { "arm": "control", "reason": "disabled" },
          "exp3_partial_routing": { "arm": "control", "reason": "not_targeted" } }
        """;

    private static StoredRoutingOrder Stored(string armsJson = armsJson, string? searchMode = "exact")
        => new(
            Guid.CreateVersion7(), "ORD-1", "hash", "10015", false, "routed", true, armsJson, "cfg", searchMode,
            new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero), [], []);

    [Fact]
    public void Map_AllThreeExperiments_MapsArmAndReasonUnderTheSnakeCaseNames()
    {
        var diagnostics = DiagnosticsMapper.Map(Stored());

        Assert.Equal(
            ["exp1_consolidation_vs_quality", "exp2_unrated_suppliers", "exp3_partial_routing"],
            diagnostics.ExperimentArms.Keys);
        Assert.Equal(new ExperimentDiagnostic("treatment", "bucketed"), diagnostics.ExperimentArms["exp1_consolidation_vs_quality"]);
        Assert.Equal(new ExperimentDiagnostic("control", "disabled"), diagnostics.ExperimentArms["exp2_unrated_suppliers"]);
        Assert.Equal(new ExperimentDiagnostic("control", "not_targeted"), diagnostics.ExperimentArms["exp3_partial_routing"]);
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("greedy")]
    public void Map_SearchMode_IsCarriedOver(string mode)
        => Assert.Equal(mode, DiagnosticsMapper.Map(Stored(searchMode: mode)).SearchMode);

    [Fact]
    public void Map_NoSearchMode_StaysNullAndIsNotSerialized()
    {
        var diagnostics = DiagnosticsMapper.Map(Stored(searchMode: null));

        Assert.Null(diagnostics.SearchMode);
        var json = JsonSerializer.SerializeToNode(diagnostics, wireOptions)!.AsObject();
        Assert.False(json.ContainsKey("search_mode"));
        Assert.True(json.ContainsKey("experiment_arms"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{ "exp1_consolidation_vs_quality": { "arm": "control", "reason": "disabled" }, "exp2_unrated_suppliers": { "arm": "control", "reason": "disabled" } }""")]
    [InlineData("""{ "exp1_consolidation_vs_quality": { "arm": "control" }, "exp2_unrated_suppliers": { "arm": "control", "reason": "disabled" }, "exp3_partial_routing": { "arm": "control", "reason": "disabled" } }""")]
    public void Map_DamagedOrIncompleteArms_Throws(string armsJson)
        => Assert.Throws<InvalidOperationException>(() => DiagnosticsMapper.Map(Stored(armsJson)));

    [Fact]
    public void Serialize_Diagnostics_UsesSnakeCaseAndHoldsNothingElse()
    {
        var json = JsonSerializer.SerializeToNode(DiagnosticsMapper.Map(Stored()), wireOptions)!.AsObject();

        Assert.Equal(["experiment_arms", "search_mode"], json.Select(pair => pair.Key));
        Assert.Equal("treatment", (string?)json["experiment_arms"]!["exp1_consolidation_vs_quality"]!["arm"]);
        Assert.Equal("bucketed", (string?)json["experiment_arms"]!["exp1_consolidation_vs_quality"]!["reason"]);
        Assert.Equal("exact", (string?)json["search_mode"]);
    }

    [Fact]
    public void RouteResponse_NoDiagnostics_OmitsTheProperty()
    {
        var json = JsonSerializer.SerializeToNode(new RouteResponse { Status = RoutingStatus.NotRouted }, wireOptions)!.AsObject();

        Assert.False(json.ContainsKey("diagnostics"));
    }

    [Fact]
    public void RouteResponse_WithDiagnostics_WritesThem()
    {
        var response = new RouteResponse { Status = RoutingStatus.NotRouted, Diagnostics = DiagnosticsMapper.Map(Stored()) };

        var json = JsonSerializer.SerializeToNode(response, wireOptions)!.AsObject();

        Assert.True(json["diagnostics"] is JsonObject);
        Assert.Empty(response.Routing);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public void StoredOrderResponse_DiagnosticsOmittedWhenNullAndWrittenWhenSet()
    {
        var stored = Stored();
        var without = StoredOrderResponseMapper.Map(stored);
        var with = without with { Diagnostics = DiagnosticsMapper.Map(stored) };

        Assert.False(JsonSerializer.SerializeToNode(without, wireOptions)!.AsObject().ContainsKey("diagnostics"));
        Assert.True(JsonSerializer.SerializeToNode(with, wireOptions)!.AsObject()["diagnostics"] is JsonObject);
        Assert.Empty(with.Items);
        Assert.Empty(with.Routing);
        Assert.Empty(with.Warnings);
    }

    [Fact]
    public void Diagnostics_NullArms_StayEmptyNotNull()
        => Assert.Empty(new Diagnostics { ExperimentArms = null! }.ExperimentArms);
}
