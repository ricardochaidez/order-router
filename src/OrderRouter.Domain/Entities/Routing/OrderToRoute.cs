using OrderRouter.Domain.Entities.ReferenceData;

namespace OrderRouter.Domain.Entities.Routing;

/// <summary>
/// The order as the routing rules see it: already validated and with products resolved. The Application layer
/// builds it from its command, so the Domain never depends on the Application (DIP).
/// </summary>
/// <param name="CustomerZip">The delivery ZIP.</param>
/// <param name="MailOrder">Whether only mail-capable suppliers may be used (FR-08).</param>
/// <param name="Lines">At least one line, in request order.</param>
public sealed record OrderToRoute(ZipCode CustomerZip, bool MailOrder, IReadOnlyList<OrderLine> Lines);
