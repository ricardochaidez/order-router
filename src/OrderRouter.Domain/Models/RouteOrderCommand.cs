using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Domain.Models;

/// <summary>
/// An order that already passed request validation (FR-03). The Api validates the wire request at the edge;
/// <see cref="Create"/> still guards the invariants as defence in depth, not as a second rule set.
/// </summary>
/// <param name="OrderId">The caller's order id (non-empty).</param>
/// <param name="CustomerZip">The delivery ZIP.</param>
/// <param name="MailOrder">Whether only mail-capable suppliers may be used (FR-08).</param>
/// <param name="Lines">At least one and at most <see cref="MaxLines"/> lines, in request order.</param>
/// <param name="CallerId">The authenticated caller, or <c>null</c> when the token has no caller claim (experiment assignment, AB-01).</param>
public sealed record RouteOrderCommand(string OrderId, ZipCode CustomerZip, bool MailOrder, IReadOnlyList<RouteOrderLine> Lines, string? CallerId = null)
{
    /// <summary>
    /// The most lines one order may have: a request-size limit, since every line is a stored row. Real DME orders have
    /// a handful of lines (the production-like sample tops out at 6), so 100 leaves ample headroom. It is unrelated to
    /// the plan search's category limit; an order over that one is held back by <c>OrderRoutingService</c>.
    /// </summary>
    public const int MaxLines = 100;

    /// <summary>Builds a command from validated values.</summary>
    /// <param name="orderId">The caller's order id.</param>
    /// <param name="customerZip">Exactly 5 digits.</param>
    /// <param name="mailOrder">Mail-order flag.</param>
    /// <param name="lines">At least one and at most <see cref="MaxLines"/> lines.</param>
    /// <param name="callerId">The authenticated caller, if known.</param>
    /// <exception cref="ArgumentException">An invariant is violated, which means the edge validation was bypassed.</exception>
    public static RouteOrderCommand Create(string orderId, string customerZip, bool mailOrder, IReadOnlyList<RouteOrderLine> lines, string? callerId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new ArgumentException("An order needs at least one line.", nameof(lines));
        }

        if (lines.Count > MaxLines)
        {
            throw new ArgumentException($"An order can have at most {MaxLines} lines (received {lines.Count}).", nameof(lines));
        }

        foreach (var line in lines)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(line.ProductCode);
            ArgumentOutOfRangeException.ThrowIfLessThan(line.Quantity, 1);
        }

        return new RouteOrderCommand(orderId, ZipCode.Parse(customerZip), mailOrder, lines, callerId);
    }
}
