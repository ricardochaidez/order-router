using System.Text.Json;
using System.Text.Json.Nodes;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Domain.Eligibility;
using OrderRouter.Domain.Routing;
using OrderRouter.Domain.Routing.Experiments;
using OrderRouter.Domain.Routing.PlanSearch;

namespace OrderRouter.Application.Routing.Storage;

/// <summary>
/// Builds the stored order from the request, the routing decision and the response that goes back to the caller
/// (ADR-0007). The response is the single source of the status, the unfulfilled messages and the warnings, so
/// what is stored always equals what is returned. It holds no business rule.
/// </summary>
public static class StoredRoutingOrderFactory
{
    /// <summary>Builds the stored order.</summary>
    /// <param name="id">The new server-generated id.</param>
    /// <param name="command">The validated request; its lines give the item rows, in request order.</param>
    /// <param name="callerHash">The hashed caller, or <see langword="null"/> when there is no caller ID.</param>
    /// <param name="createdAt">When the order is stored.</param>
    /// <param name="decision">The routing decision.</param>
    /// <param name="response">The mapped response (without <c>id</c>).</param>
    /// <exception cref="InvalidOperationException">
    /// The decision's routed and unfulfilled lines cannot be matched to the requested lines. This is a programming
    /// error; it becomes a 500 and nothing is stored (fail safe: no guessed line).
    /// </exception>
    public static StoredRoutingOrder Create(
        Guid id,
        RouteOrderCommand command,
        string? callerHash,
        DateTimeOffset createdAt,
        RoutingDecision decision,
        RouteResponse response)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(response);

        return new StoredRoutingOrder(
            id,
            command.OrderId,
            callerHash,
            command.CustomerZip.Value,
            command.MailOrder,
            SnakeCase(response.Status.ToString()),
            response.Feasible,
            ExperimentArmsJson(decision.Assignment),
            decision.Assignment.ConfigVersion,
            decision.SearchMode is { } mode ? SnakeCase(mode.ToString()) : null,
            createdAt,
            Items(command, decision, response),
            response.Warnings);
    }

    // Routed and unfulfilled lines are both in request order, so one ordered walk matches them to the requested
    // lines. This also works for duplicate product codes (DEV-10): equal codes share a category, so they always
    // land in the same bucket. A line in neither bucket was held back by all-or-nothing.
    private static List<StoredOrderItem> Items(RouteOrderCommand command, RoutingDecision decision, RouteResponse response)
    {
        var routed = decision.RoutedLines;
        var unfulfilled = decision.Unfulfilled;
        var items = new List<StoredOrderItem>(command.Lines.Count);
        int r = 0, u = 0;

        for (var i = 0; i < command.Lines.Count; i++)
        {
            var line = command.Lines[i];
            var lineNumber = i + 1;

            if (r < routed.Count && Matches(routed[r].ProductCode, routed[r].Quantity, line))
            {
                var routedLine = routed[r++];
                items.Add(new StoredOrderItem(
                    lineNumber, line.ProductCode, line.Quantity, routedLine.Category.Value,
                    routedLine.Supplier.Id, routedLine.Supplier.Name, ModeText(routedLine.Mode), null, null));
            }
            else if (u < unfulfilled.Count && Matches(unfulfilled[u].ProductCode, unfulfilled[u].Quantity, line))
            {
                var missing = unfulfilled[u];
                items.Add(new StoredOrderItem(
                    lineNumber, line.ProductCode, line.Quantity, missing.Category?.Value,
                    null, null, null, missing.ReasonCode, response.UnfulfilledItems[u].Message));
                u++;
            }
            else
            {
                items.Add(new StoredOrderItem(lineNumber, line.ProductCode, line.Quantity, null, null, null, null, null, null));
            }
        }

        if (r != routed.Count || u != unfulfilled.Count)
        {
            throw new InvalidOperationException("The routed and unfulfilled lines do not match the requested lines.");
        }

        return items;
    }

    private static bool Matches(string productCode, int quantity, RouteOrderLine line)
        => quantity == line.Quantity && string.Equals(productCode, line.ProductCode, StringComparison.Ordinal);

    // AB-04: the arm and the reason of each experiment, under the experiment's own name.
    private static string ExperimentArmsJson(ExperimentAssignment assignment)
    {
        var arms = assignment.Arms;
        var reasons = assignment.Reasons;

        return new JsonObject
        {
            [SnakeCase(nameof(ExperimentArms.Exp1ConsolidationVsQuality))] = Experiment(arms.Exp1ConsolidationVsQuality, reasons.Exp1ConsolidationVsQuality),
            [SnakeCase(nameof(ExperimentArms.Exp2UnratedSuppliers))] = Experiment(arms.Exp2UnratedSuppliers, reasons.Exp2UnratedSuppliers),
            [SnakeCase(nameof(ExperimentArms.Exp3PartialRouting))] = Experiment(arms.Exp3PartialRouting, reasons.Exp3PartialRouting),
        }.ToJsonString();
    }

    private static JsonObject Experiment(ExperimentArm arm, ExposureReason reason)
        => new() { ["arm"] = SnakeCase(arm.ToString()), ["reason"] = SnakeCase(reason.ToString()) };

    private static string ModeText(FulfillmentMode mode)
        => mode switch
        {
            FulfillmentMode.Local => "local",
            FulfillmentMode.Mail => "mail",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown fulfillment mode."),
        };

    private static string SnakeCase(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}
