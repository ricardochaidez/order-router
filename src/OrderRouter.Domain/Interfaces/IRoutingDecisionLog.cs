using OrderRouter.Domain.Models;
namespace OrderRouter.Domain.Interfaces;

/// <summary>
/// Writes the decision log event of a stored routing order (AB-04, ADR-0005). It is the data source for later A/B
/// analysis. Callers must invoke it only after the order is saved, because the event carries the stored <c>id</c>.
/// </summary>
public interface IRoutingDecisionLog
{
    /// <summary>Records one <c>RoutingDecisionMade</c> event. It must never throw: observability cannot fail a saved order.</summary>
    /// <param name="entry">The event data.</param>
    void Record(RoutingDecisionLogEntry entry);
}
