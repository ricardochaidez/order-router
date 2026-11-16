using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderRouter.Api.Authentication;
using OrderRouter.Api.Routing;
using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Experiments;

namespace OrderRouter.Api.Controllers;

/// <summary>
/// Routes orders and reads stored orders back. <c>POST /api/v1/route</c> (canonical) and <c>POST /api/route</c> (alias
/// pinned to v1) are two attribute routes on the same action, so they share one handler, auth policy, validation and
/// response contract and cannot drift (ADR-0004). <c>GET /api/v1/route/{id}</c> reads a stored order back (FR-21).
/// There is deliberately no GET under <c>/api/route</c>: the alias is POST-only (ADR-0004).
/// </summary>
/// <param name="routeOrderUseCase">Validates, routes and stores an order.</param>
/// <param name="getStoredOrderUseCase">Reads a stored order back for its caller.</param>
[ApiController]
[Tags("Routing")]
// The RouteCaller policy is also the fallback; naming it here makes the intent explicit on the routing endpoints.
[Authorize(Policy = AuthorizationPolicyNames.RouteCaller)]
public sealed class RouteController(IRouteOrderUseCase routeOrderUseCase, IGetStoredOrderUseCase getStoredOrderUseCase) : ControllerBase
{
    /// <summary>Route name (and OpenAPI operation id) of the canonical POST.</summary>
    public const string RouteOrderV1Name = "RouteOrderV1";

    /// <summary>Route name (and OpenAPI operation id) of the alias POST.</summary>
    public const string RouteOrderAliasName = "RouteOrderAlias";

    /// <summary>Route name (and OpenAPI operation id) of the read endpoint.</summary>
    public const string GetStoredOrderName = "GetStoredOrderV1";

    /// <summary>
    /// Validates the order, assigns each line item to a supplier and stores the decision. Answers <c>201 Created</c>
    /// with a <c>Location</c> header on the v1 GET path; an invalid request is a <c>400</c> in the same envelope.
    /// </summary>
    /// <param name="request">The order. Already validated by <see cref="ApiControllerAttribute"/> before this runs (FR-03).</param>
    /// <param name="cancellationToken">Aborts when the caller disconnects.</param>
    /// <returns>The routing decision of the stored order.</returns>
    /// <exception cref="InvalidOperationException">The use case returned no stored order id (a programming error, so a 500).</exception>
    [HttpPost("api/v1/route", Name = RouteOrderV1Name)]
    [HttpPost("api/route", Name = RouteOrderAliasName)]
    [RouteEnvelope]
    // The examples and the Location header are added by RouteOpenApiTransformer.
    [ProducesResponseType<RouteResponse>(StatusCodes.Status201Created, MediaTypeNames.Application.Json)]
    [ProducesResponseType<RouteResponse>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<RouteResponse>> RouteOrder([FromBody] RouteRequest request, CancellationToken cancellationToken)
    {
        // The request already passed validation at the edge (FR-03); the command factory only guards invariants.
        var command = RouteOrderCommand.Create(
            request.OrderId!,
            request.CustomerZip!,
            request.MailOrder,
            [.. request.Items!.Select(item => new RouteOrderLine(item!.ProductCode!, item.Quantity!.Value))],
            // The experiment unit (AB-01): sub, then client_id, then azp; null falls back to the order ID. Never logged (NFR-11).
            CallerIdentity.Resolve(User));

        var response = await routeOrderUseCase.RouteAsync(command, cancellationToken);

        // SC-30: a valid POST creates a stored order, so it answers 201 Created. The id is set by the use case after the
        // save; without it there is nothing to point at, which is a programming error and becomes a 500.
        var id = response.Id ?? throw new InvalidOperationException("The routing use case returned a response without a stored order id.");

        // Location always points at the v1 GET, also for the alias, which is POST-only (ADR-0004, ADR-0007). RouteUrl
        // (not CreatedAtRoute) keeps the header a relative path, as it has always been.
        var location = Url.RouteUrl(GetStoredOrderName, new { id })
            ?? throw new InvalidOperationException($"No URL could be generated for route '{GetStoredOrderName}'.");

        return Created(location, response);
    }

    /// <summary>
    /// Returns the order stored by <c>POST /api/v1/route</c>, exactly as it was decided then. Only the caller that
    /// created it can read it. Everything else is the same <c>404</c>, so existence is never revealed (FR-21, SC-31).
    /// </summary>
    /// <param name="id">The stored order id: a UUID in the canonical hyphenated form.</param>
    /// <param name="cancellationToken">Aborts when the caller disconnects.</param>
    /// <returns>The stored order, or a 404 ProblemDetails.</returns>
    [HttpGet("api/v1/route/{id}", Name = GetStoredOrderName)]
    // No RouteEnvelope: that is the POST-only 400 envelope. A GET has no body to validate.
    [ProducesResponseType<StoredOrderResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<StoredOrderResponse>> GetStoredOrder(string id, CancellationToken cancellationToken)
    {
        // The id is parsed here, after authentication, and not with a {id:guid} route constraint: a constraint would turn
        // an invalid id into a bodiless 404 before auth runs, so an anonymous call to /api/v1/route/abc would not get 401.
        // Only the canonical hyphenated form is accepted.
        if (!Guid.TryParseExact(id, "D", out var orderId))
        {
            return OrderNotFound();
        }

        var order = await getStoredOrderUseCase.GetAsync(orderId, CallerIdentity.Resolve(User), cancellationToken);

        return order is null ? OrderNotFound() : Ok(order);
    }

    // One 404 for every case that is not "this caller's stored order" (unknown id, not a UUID, another caller's order,
    // an order stored without a caller). Same status, title and detail, so nothing reveals whether an id exists.
    private ObjectResult OrderNotFound()
        => Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Order not found",
            detail: "No stored order was found for this id.");
}
