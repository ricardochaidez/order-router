using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json;
using OrderRouter.Api.ViewModels.Response;

namespace OrderRouter.Tests.Api.ViewModels.Response;

public class RouteResponseTests
{
    // The same policy the API configures (FR-01, ADR-0004).
    private static readonly JsonSerializerOptions wireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    [Theory]
    [InlineData(RoutingStatus.Routed, true)]
    [InlineData(RoutingStatus.PartiallyRouted, false)]
    [InlineData(RoutingStatus.NotRouted, false)]
    public void Feasible_ForStatus_IsTrueOnlyWhenRouted(RoutingStatus status, bool expected)
    {
        var response = new RouteResponseViewModel { Status = status };

        Assert.Equal(expected, response.Feasible);
    }

    [Fact]
    public void New_NothingAssigned_AllCollectionsAreEmptyNotNull()
    {
        var response = new RouteResponseViewModel { Status = RoutingStatus.NotRouted };

        Assert.Empty(response.Routing);
        Assert.Empty(response.UnfulfilledItems);
        Assert.Empty(response.Warnings);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public void New_NullsAssigned_CollectionsStayEmpty()
    {
        var response = new RouteResponseViewModel
        {
            Status = RoutingStatus.NotRouted,
            Routing = null!,
            UnfulfilledItems = null!,
            Warnings = null!,
            Errors = null!,
        };

        Assert.NotNull(response.Routing);
        Assert.NotNull(response.UnfulfilledItems);
        Assert.NotNull(response.Warnings);
        Assert.NotNull(response.Errors);
    }

    [Fact]
    public void Invalid_WithErrors_IsNotRoutedAndCarriesEveryError()
    {
        var response = RouteResponseViewModel.Invalid(["a", "b"]);

        Assert.False(response.Feasible);
        Assert.Equal(RoutingStatus.NotRouted, response.Status);
        Assert.Equal(["a", "b"], response.Errors);
        Assert.Empty(response.Routing);
    }

    [Fact]
    public void Serialize_Invalid_UsesSpecFieldNamesAndOmitsIdAndOrderId()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(RouteResponseViewModel.Invalid(["customer_zip is required."]), wireOptions))!.AsObject();

        Assert.Equal(
            ["feasible", "status", "routing", "unfulfilled_items", "warnings", "errors"],
            json.Select(property => property.Key).ToArray());
        Assert.Equal("not_routed", (string?)json["status"]);
        Assert.False((bool)json["feasible"]!);
        Assert.Empty(json["routing"]!.AsArray());
    }

    [Fact]
    public void Serialize_RoutedWithIds_WritesIdAndOrderIdAndNestedNames()
    {
        var id = Guid.CreateVersion7();
        var response = new RouteResponseViewModel
        {
            Id = id,
            OrderId = "ORD-1",
            Status = RoutingStatus.Routed,
            Routing = [new SupplierRoutingResponseViewModel("SUP-1", "Care", [new RoutedItemResponseViewModel("WC-STD-001", 1, "wheelchair", "local")])],
            UnfulfilledItems = [new UnfulfilledItemResponseViewModel("XX", 1, null, "UNKNOWN_PRODUCT", "missing")],
            Warnings = [new RoutingWarningResponseViewModel("DEP", "m", ["XX"], ["WC-STD-001"])],
        };

        var json = JsonNode.Parse(JsonSerializer.Serialize(response, wireOptions))!.AsObject();

        Assert.Equal(id.ToString(), (string?)json["id"]);
        Assert.Equal("ORD-1", (string?)json["order_id"]);
        Assert.True((bool)json["feasible"]!);
        Assert.Equal("SUP-1", (string?)json["routing"]![0]!["supplier_id"]);
        Assert.Equal("local", (string?)json["routing"]![0]!["items"]![0]!["fulfillment_mode"]);
        Assert.Equal("UNKNOWN_PRODUCT", (string?)json["unfulfilled_items"]![0]!["reason_code"]);
        Assert.Equal("XX", (string?)json["warnings"]![0]!["unfulfilled_product_codes"]![0]);
    }
}
