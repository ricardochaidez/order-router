using Microsoft.Extensions.Logging;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Domain.Models;

namespace OrderRouter.Infrastructure.Services;

/// <summary>
/// Writes <c>RoutingDecisionMade</c> with the source-generated <c>[LoggerMessage]</c> method (AB-04, ADR-0005, NFR-06).
/// It uses <see cref="ILogger"/> from Microsoft.Extensions.Logging.Abstractions only; the host chooses the provider.
/// </summary>
/// <param name="logger">The logger for this category.</param>
public sealed partial class LoggerRoutingDecisionLog(ILogger<LoggerRoutingDecisionLog> logger) : IRoutingDecisionLog
{
    /// <inheritdoc />
    public void Record(RoutingDecisionLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            RoutingDecisionMade(
                entry.Id, entry.OrderId, entry.CallerHash,
                entry.Exp1Arm, entry.Exp1Reason, entry.Exp2Arm, entry.Exp2Reason, entry.Exp3Arm, entry.Exp3Reason,
                entry.ChosenSuppliers, entry.TiedCandidates, entry.TiePick,
                entry.ShipmentCount, entry.PlanQuality, entry.UnratedChosen, entry.UnfulfilledCount,
                entry.SearchMode, entry.ConfigVersion, entry.CustomerZip);
        }
        catch (Exception)
        {
            // A failing log provider must not turn an order that is already stored (and about to be returned with
            // its id) into a 500. The response is the contract; the log is best-effort observability.
        }
    }

    // The placeholder names are the field names of AB-04, so analysts query the same names the requirements use.
    // The snake_case parameter names are required by the generator (placeholders must match parameters).
#pragma warning disable IDE1006
    [LoggerMessage(
        EventId = 2100,
        EventName = "RoutingDecisionMade",
        Level = LogLevel.Information,
        Message = "RoutingDecisionMade id={id} order_id={order_id} caller_hash={caller_hash} " +
                  "exp1_arm={exp1_arm} exp1_reason={exp1_reason} exp2_arm={exp2_arm} exp2_reason={exp2_reason} exp3_arm={exp3_arm} exp3_reason={exp3_reason} " +
                  "chosen_suppliers={chosen_suppliers} tied_candidates={tied_candidates} tie_pick={tie_pick} " +
                  "shipment_count={shipment_count} plan_quality={plan_quality} unrated_chosen={unrated_chosen} unfulfilled_count={unfulfilled_count} " +
                  "search_mode={search_mode} config_version={config_version} customer_zip={customer_zip}")]
    private partial void RoutingDecisionMade(
        Guid id, string order_id, string? caller_hash,
        string exp1_arm, string exp1_reason, string exp2_arm, string exp2_reason, string exp3_arm, string exp3_reason,
        string chosen_suppliers, string? tied_candidates, string? tie_pick,
        int shipment_count, decimal? plan_quality, int unrated_chosen, int unfulfilled_count,
        string? search_mode, string config_version, string customer_zip);
#pragma warning restore IDE1006
}
