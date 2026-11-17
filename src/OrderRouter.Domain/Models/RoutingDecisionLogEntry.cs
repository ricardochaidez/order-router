
namespace OrderRouter.Domain.Models;

/// <summary>
/// The data of one <c>RoutingDecisionMade</c> event (AB-04, ADR-0005). Every value is a flat scalar so log tools can
/// filter on each field. There is deliberately <b>no</b> raw caller ID and no patient data other than
/// <see cref="CustomerZip"/> (NFR-11); the caller appears only as its HMAC hash. Immutable.
/// </summary>
/// <param name="Id">The stored order id.</param>
/// <param name="OrderId">The caller's order id.</param>
/// <param name="CallerHash">HMAC hash of the caller ID; <see langword="null"/> when the token had no caller claim.</param>
/// <param name="Exp1Arm"><c>control</c> or <c>treatment</c> for EXP-1.</param>
/// <param name="Exp1Reason"><c>disabled</c>, <c>not_targeted</c> or <c>bucketed</c> for EXP-1.</param>
/// <param name="Exp2Arm">Arm of EXP-2.</param>
/// <param name="Exp2Reason">Reason for the EXP-2 arm.</param>
/// <param name="Exp3Arm">Arm of EXP-3.</param>
/// <param name="Exp3Reason">Reason for the EXP-3 arm.</param>
/// <param name="ChosenSuppliers">The routed suppliers and modes, for example <c>SUP-1:local,SUP-2:mail</c>; empty when nothing was routed.</param>
/// <param name="TiedCandidates">Only when the random tie-break ran: every tied plan as its suppliers (joined with <c>+</c>), separated by commas; otherwise <see langword="null"/>.</param>
/// <param name="TiePick">Only when the random tie-break ran: the plan that was picked, written like one tied candidate; otherwise <see langword="null"/>.</param>
/// <param name="ShipmentCount">Number of suppliers the order was routed to; 0 when nothing was routed.</param>
/// <param name="PlanQuality">The quality of the routed plan; <see langword="null"/> when nothing was routed.</param>
/// <param name="UnratedChosen">How many of the routed suppliers have no rating.</param>
/// <param name="UnfulfilledCount">How many requested lines could not be routed.</param>
/// <param name="SearchMode"><c>exact</c> or <c>greedy</c>; <see langword="null"/> when no search ran.</param>
/// <param name="ConfigVersion">The experiment configuration version in force.</param>
/// <param name="CustomerZip">The delivery ZIP, the only patient-adjacent value allowed in the log (AB-04).</param>
public sealed record RoutingDecisionLogEntry(
    Guid Id,
    string OrderId,
    string? CallerHash,
    string Exp1Arm,
    string Exp1Reason,
    string Exp2Arm,
    string Exp2Reason,
    string Exp3Arm,
    string Exp3Reason,
    string ChosenSuppliers,
    string? TiedCandidates,
    string? TiePick,
    int ShipmentCount,
    decimal? PlanQuality,
    int UnratedChosen,
    int UnfulfilledCount,
    string? SearchMode,
    string ConfigVersion,
    string CustomerZip);
