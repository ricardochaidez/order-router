using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Application.Routing;

/// <summary>One requested line item of a validated order.</summary>
/// <param name="ProductCode">The product code exactly as requested (whitespace and case are handled at lookup, FR-05).</param>
/// <param name="Quantity">At least 1.</param>
public sealed record RouteOrderLine(string ProductCode, int Quantity);

/// <summary>
/// An order that already passed request validation (FR-03). The Api validates the wire request at the edge;
/// <see cref="Create"/> still guards the invariants as defence in depth, not as a second rule set.
/// </summary>
/// <param name="OrderId">The caller's order id (non-empty).</param>
/// <param name="CustomerZip">The delivery ZIP.</param>
/// <param name="MailOrder">Whether only mail-capable suppliers may be used (FR-08).</param>
/// <param name="Lines">At least one line, in request order.</param>
public sealed record RouteOrderCommand(string OrderId, ZipCode CustomerZip, bool MailOrder, IReadOnlyList<RouteOrderLine> Lines)
{
    /// <summary>Builds a command from validated values.</summary>
    /// <param name="orderId">The caller's order id.</param>
    /// <param name="customerZip">Exactly 5 digits.</param>
    /// <param name="mailOrder">Mail-order flag.</param>
    /// <param name="lines">At least one line.</param>
    /// <exception cref="ArgumentException">An invariant is violated, which means the edge validation was bypassed.</exception>
    public static RouteOrderCommand Create(string orderId, string customerZip, bool mailOrder, IReadOnlyList<RouteOrderLine> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new ArgumentException("An order needs at least one line.", nameof(lines));
        }

        foreach (var line in lines)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(line.ProductCode);
            ArgumentOutOfRangeException.ThrowIfLessThan(line.Quantity, 1);
        }

        return new RouteOrderCommand(orderId, ZipCode.Parse(customerZip), mailOrder, lines);
    }
}
