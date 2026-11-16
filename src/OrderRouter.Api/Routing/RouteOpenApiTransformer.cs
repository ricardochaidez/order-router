using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace OrderRouter.Api.Routing;

/// <summary>
/// Documents the route operations in the <c>v1</c> OpenAPI document: summary, description, and request/response
/// examples (AC10). The alias is described as pinned to v1 so callers know it never moves to a later version
/// (ADR-0004). The 401 response and security requirement come from <c>SecuritySchemeTransformer</c>.
/// </summary>
internal sealed class RouteOpenApiTransformer : IOpenApiOperationTransformer
{
    private const string json = "application/json";
    private const string problemJson = "application/problem+json";

    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IEndpointNameMetadata>().Any(name => name.EndpointName == RouteEndpoints.GetStoredOrderName))
        {
            DescribeGetStoredOrder(operation);

            return Task.CompletedTask;
        }

        if (!metadata.OfType<RouteEnvelopeMetadata>().Any())
        {
            return Task.CompletedTask;
        }

        var isAlias = metadata.OfType<IEndpointNameMetadata>().Any(name => name.EndpointName == RouteEndpoints.AliasName);

        operation.Summary = isAlias ? "Route an order (alias of the v1 endpoint)" : "Route an order";
        operation.Description = isAlias
            ? "Alias of `POST /api/v1/route`, permanently pinned to v1. It shares the handler, authentication, validation " +
              "and response contract with the canonical path and never moves to a later version."
            : "Validates the order, assigns each line item to a supplier and stores the decision. Every validation error is returned at once in " +
              "the same envelope as a successful response. Unknown request fields are ignored. The first response is authoritative.";

        if (operation.RequestBody?.Content is { } requestContent && requestContent.TryGetValue(json, out var request))
        {
            request.Example = RequestExample();
        }

        SetExamples(
            operation,
            "201",
            "The order was valid, routing ran and the decision was stored. `id` is the stored order. `status` and `feasible` say whether every item was routed; a business outcome such as \"could not route\" is a 201 with a body, not an error.",
            ("routed", "Every item is assigned to a supplier.", RoutedExample()),
            ("partially_routed", "Only with the EXP-3 treatment: the items that can be routed are, the rest are listed in `unfulfilled_items` and `feasible` is false.", PartiallyRoutedExample()),
            ("not_routed", "Nothing could be routed (here: an unknown product).", NotRoutedExample()),
            ("routed_with_diagnostics", "Only when `Routing:ExposeDiagnostics` is on (off by default): the optional `diagnostics` object is added. Clients must not depend on it.", WithDiagnostics(RoutedExample())));
        AddLocationHeader(operation);
        SetExample(operation, "400", "The request is invalid. Every error is listed; the body uses the same envelope as a success.", InvalidExample());

        if (operation.Responses is not null && operation.Responses.TryGetValue("500", out var fault))
        {
            fault.Description = "Unexpected fault. ProblemDetails (RFC 9457) with no internal details.";
        }

        return Task.CompletedTask;
    }

    // FR-21, SC-31: documents GET /api/v1/route/{id}. It has no 400 envelope (that belongs to POST); an id that is not a
    // UUID is a 404 like any other id that is not the caller's. The 401 comes from SecuritySchemeTransformer.
    private static void DescribeGetStoredOrder(OpenApiOperation operation)
    {
        operation.Summary = "Get a stored order";
        operation.Description = "Returns the order stored by `POST /api/v1/route`: the request details, every requested item and the decision exactly as " +
            "it was returned then. It never routes again, so the supplier assignment does not change between reads. Only the caller that created " +
            "the order can read it. Unknown ids, ids that are not UUIDs, other callers' orders and orders stored without a caller ID all return " +
            "the same 404, so existence is never revealed. There is no GET on the `/api/route` alias.";

        if (operation.Parameters?.FirstOrDefault(parameter => parameter.Name == "id") is OpenApiParameter id)
        {
            id.Description = "The stored order id (UUID, the `id` of the POST response and the end of its `Location` header).";
        }

        SetExamples(
            operation,
            "200",
            "The stored order. `items` lists every requested line in request order, including lines that were not routed. The optional `diagnostics` object is present only when `Routing:ExposeDiagnostics` is on (off by default); clients must not depend on it.",
            ("stored_order", "The default body, without diagnostics.", StoredOrderExample()),
            ("stored_order_with_diagnostics", "With `Routing:ExposeDiagnostics` on: the same stored arms and search mode that POST showed for this order.", WithDiagnostics(StoredOrderExample())));
        SetExample(
            operation,
            "404",
            "No stored order for this id and caller. The same body is returned for an unknown id, an id that is not a UUID, another caller's order and an order stored without a caller ID. ProblemDetails (RFC 9457).",
            NotFoundExample(),
            problemJson);

        if (operation.Responses is not null && operation.Responses.TryGetValue("500", out var fault))
        {
            fault.Description = "Unexpected fault. ProblemDetails (RFC 9457) with no internal details.";
        }
    }

    // SC-30: the Location header points at the stored order on the v1 path, also on the alias.
    private static void AddLocationHeader(OpenApiOperation operation)
    {
        // The interface exposes Headers read-only; the generator creates the concrete type, which allows it.
        if (operation.Responses is null || !operation.Responses.TryGetValue("201", out var created) || created is not OpenApiResponse response)
        {
            return;
        }

        response.Headers ??= new Dictionary<string, IOpenApiHeader>();
        response.Headers["Location"] = new OpenApiHeader
        {
            Description = "URL of the stored order: `/api/v1/route/{id}`. It always uses the v1 path, also when the alias was called.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        };
    }

    private static void SetExample(OpenApiOperation operation, string status, string description, JsonNode example, string mediaType = json)
    {
        if (operation.Responses is null || !operation.Responses.TryGetValue(status, out var response))
        {
            return;
        }

        response.Description = description;

        if (response.Content is not null && response.Content.TryGetValue(mediaType, out var content))
        {
            content.Example = example;
        }
    }

    private static void SetExamples(OpenApiOperation operation, string status, string description, params (string Name, string Summary, JsonNode Value)[] examples)
    {
        if (operation.Responses is null || !operation.Responses.TryGetValue(status, out var response))
        {
            return;
        }

        response.Description = description;

        if (response.Content is not null && response.Content.TryGetValue(json, out var content))
        {
            content.Examples = examples.ToDictionary(
                example => example.Name,
                example => (IOpenApiExample)new OpenApiExample { Summary = example.Summary, Value = example.Value });
        }
    }

    private static JsonNode RoutedExample() => JsonNode.Parse("""
        {
          "id": "0193a1b2-7c3d-7e4f-8a9b-0c1d2e3f4a5b",
          "order_id": "ORD-EXAMPLE",
          "feasible": true,
          "status": "routed",
          "routing": [
            { "supplier_id": "SUP-0810", "supplier_name": "Example Supplier",
              "items": [
                { "product_code": "WC-STD-001", "quantity": 1, "category": "wheelchair", "fulfillment_mode": "local" },
                { "product_code": "OX-PORT-024", "quantity": 1, "category": "oxygen", "fulfillment_mode": "local" }
              ] }
          ],
          "unfulfilled_items": [],
          "warnings": [],
          "errors": []
        }
        """)!;

    private static JsonNode StoredOrderExample() => JsonNode.Parse("""
        {
          "id": "0193a1b2-7c3d-7e4f-8a9b-0c1d2e3f4a5b",
          "order_id": "ORD-EXAMPLE",
          "customer_zip": "10015",
          "mail_order": false,
          "created_at": "2026-11-15T10:00:00Z",
          "items": [
            { "product_code": "WC-STD-001", "quantity": 1 },
            { "product_code": "OX-PORT-024", "quantity": 1 }
          ],
          "feasible": true,
          "status": "routed",
          "routing": [
            { "supplier_id": "SUP-0810", "supplier_name": "Example Supplier",
              "items": [
                { "product_code": "WC-STD-001", "quantity": 1, "category": "wheelchair", "fulfillment_mode": "local" },
                { "product_code": "OX-PORT-024", "quantity": 1, "category": "oxygen", "fulfillment_mode": "local" }
              ] }
          ],
          "unfulfilled_items": [],
          "warnings": [],
          "errors": []
        }
        """)!;

    // AB-05, OQ-08: the optional diagnostics object (arms and reasons per experiment, search mode when a search ran).
    private static JsonNode WithDiagnostics(JsonNode body)
    {
        body["diagnostics"] = JsonNode.Parse("""
            {
              "experiment_arms": {
                "exp1_consolidation_vs_quality": { "arm": "control", "reason": "disabled" },
                "exp2_unrated_suppliers": { "arm": "control", "reason": "disabled" },
                "exp3_partial_routing": { "arm": "control", "reason": "disabled" }
              },
              "search_mode": "exact"
            }
            """);

        return body;
    }

    private static JsonNode NotFoundExample() => JsonNode.Parse("""
        {
          "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
          "title": "Order not found",
          "status": 404,
          "detail": "No stored order was found for this id."
        }
        """)!;

    private static JsonNode PartiallyRoutedExample() => JsonNode.Parse("""
        {
          "id": "0193a1b2-7c3d-7e4f-8a9b-0c1d2e3f4a5d",
          "order_id": "ORD-EXAMPLE",
          "feasible": false,
          "status": "partially_routed",
          "routing": [
            { "supplier_id": "SUP-0810", "supplier_name": "Example Supplier",
              "items": [
                { "product_code": "WC-STD-001", "quantity": 1, "category": "wheelchair", "fulfillment_mode": "local" }
              ] }
          ],
          "unfulfilled_items": [
            { "product_code": "XX-UNKNOWN-999", "quantity": 1, "category": null,
              "reason_code": "UNKNOWN_PRODUCT", "message": "Product code 'XX-UNKNOWN-999' was not found in the catalog." }
          ],
          "warnings": [],
          "errors": [ "Product code 'XX-UNKNOWN-999' was not found in the catalog." ]
        }
        """)!;

    // priority and notes show that unknown fields are accepted and ignored (DQ-15).
    private static JsonNode RequestExample() => JsonNode.Parse("""
        {
          "order_id": "ORD-EXAMPLE",
          "customer_zip": "10015",
          "mail_order": false,
          "priority": "high",
          "notes": "Unknown fields such as these are ignored.",
          "items": [
            { "product_code": "WC-STD-001", "quantity": 1 },
            { "product_code": "OX-PORT-024", "quantity": 1 }
          ]
        }
        """)!;

    private static JsonNode NotRoutedExample() => JsonNode.Parse("""
        {
          "id": "0193a1b2-7c3d-7e4f-8a9b-0c1d2e3f4a5c",
          "order_id": "ORD-EXAMPLE",
          "feasible": false,
          "status": "not_routed",
          "routing": [],
          "unfulfilled_items": [
            { "product_code": "XX-UNKNOWN-999", "quantity": 1, "category": null,
              "reason_code": "UNKNOWN_PRODUCT", "message": "Product code 'XX-UNKNOWN-999' was not found in the catalog." }
          ],
          "warnings": [],
          "errors": [ "Product code 'XX-UNKNOWN-999' was not found in the catalog." ]
        }
        """)!;

    private static JsonNode InvalidExample() => JsonNode.Parse("""
        {
          "feasible": false,
          "status": "not_routed",
          "routing": [],
          "unfulfilled_items": [],
          "warnings": [],
          "errors": [
            "customer_zip is required.",
            "items must contain at least one item."
          ]
        }
        """)!;
}
