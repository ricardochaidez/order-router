using System.Text.Json;
using System.Text.Json.Nodes;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Storage;

namespace OrderRouter.Application.Routing.Mappers;

/// <summary>
/// Builds <see cref="Diagnostics"/> from a stored order. POST and GET both call it with the stored record, so the two
/// can never show different values for the same order (AB-05, ADR-0004). It holds no business rule and does not
/// look at the <c>Routing:ExposeDiagnostics</c> flag; the use cases do.
/// </summary>
public static class DiagnosticsMapper
{
    // The experiments every stored order has (StoredRoutingOrderFactory). A missing one means the record is damaged.
    private static readonly string[] experimentNames =
    [
        "exp1_consolidation_vs_quality",
        "exp2_unrated_suppliers",
        "exp3_partial_routing",
    ];

    /// <summary>Maps the stored arms and search mode.</summary>
    /// <param name="order">The stored order.</param>
    /// <exception cref="InvalidOperationException">
    /// The stored arms are not valid JSON or miss an experiment. It becomes a 500 with no internals (fail safe:
    /// an arm is never guessed).
    /// </exception>
    public static Diagnostics Map(StoredRoutingOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        JsonObject root;

        try
        {
            root = JsonNode.Parse(order.ExperimentArmsJson) as JsonObject
                ?? throw new InvalidOperationException("The stored experiment arms are not a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The stored experiment arms cannot be read.", exception);
        }

        // The three known experiments, in a fixed order so the body is stable.
        var arms = new Dictionary<string, ExperimentDiagnostic>();

        foreach (var name in experimentNames)
        {
            if (root[name] is not JsonObject entry
                || entry["arm"] is not JsonValue arm || !arm.TryGetValue<string>(out var armText)
                || entry["reason"] is not JsonValue reason || !reason.TryGetValue<string>(out var reasonText))
            {
                throw new InvalidOperationException($"The stored experiment arms are incomplete: {name}.");
            }

            arms[name] = new ExperimentDiagnostic(armText, reasonText);
        }

        return new Diagnostics { ExperimentArms = arms, SearchMode = order.SearchMode };
    }
}
