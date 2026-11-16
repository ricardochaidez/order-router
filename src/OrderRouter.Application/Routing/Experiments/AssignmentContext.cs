using OrderRouter.Domain.ReferenceData;

namespace OrderRouter.Application.Routing.Experiments;

/// <summary>What an assigner needs to know about one request (ADR-0005).</summary>
/// <param name="CallerId">The authenticated caller (JWT <c>sub</c>, or the client ID), or <c>null</c> when unknown (AB-01).</param>
/// <param name="CustomerZip">The delivery ZIP, used for regional targeting (SC-26).</param>
/// <param name="OrderId">The caller's order ID; the randomization unit when there is no caller.</param>
public sealed record AssignmentContext(string? CallerId, ZipCode CustomerZip, string OrderId);
