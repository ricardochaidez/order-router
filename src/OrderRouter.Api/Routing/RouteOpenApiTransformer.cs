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

    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        if (!metadata.OfType<RouteEnvelopeMetadata>().Any())
        {
            return Task.CompletedTask;
        }

        var isAlias = metadata.OfType<IEndpointNameMetadata>().Any(name => name.EndpointName == RouteEndpoints.AliasName);

        operation.Summary = isAlias ? "Route an order (alias of the v1 endpoint)" : "Route an order";
        operation.Description = isAlias
            ? "Alias of `POST /api/v1/route`, permanently pinned to v1. It shares the handler, authentication, validation " +
              "and response contract with the canonical path and never moves to a later version."
            : "Validates the order and assigns each line item to a supplier. Every validation error is returned at once in " +
              "the same envelope as a successful response. Unknown request fields are ignored. The first response is authoritative.";

        if (operation.RequestBody?.Content is { } requestContent && requestContent.TryGetValue(json, out var request))
        {
            request.Example = RequestExample();
        }

        SetExamples(
            operation,
            "200",
            "The order was valid and routing ran. `status` and `feasible` say whether every item was routed; a business outcome such as \"could not route\" is a 200 with a body, not an error.",
            ("routed", "Every item is assigned to a supplier.", RoutedExample()),
            ("not_routed", "Nothing could be routed (here: an unknown product).", NotRoutedExample()));
        SetExample(operation, "400", "The request is invalid. Every error is listed; the body uses the same envelope as a success.", InvalidExample());

        if (operation.Responses is not null && operation.Responses.TryGetValue("500", out var fault))
        {
            fault.Description = "Unexpected fault. ProblemDetails (RFC 9457) with no internal details.";
        }

        return Task.CompletedTask;
    }

    private static void SetExample(OpenApiOperation operation, string status, string description, JsonNode example)
    {
        if (operation.Responses is null || !operation.Responses.TryGetValue(status, out var response))
        {
            return;
        }

        response.Description = description;

        if (response.Content is not null && response.Content.TryGetValue(json, out var content))
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
