using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using OrderRouter.Api.Authentication;
using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Experiments;

namespace OrderRouter.Api.Routing;

/// <summary>
/// Maps <c>POST /api/v1/route</c> (canonical) and <c>POST /api/route</c> (alias pinned to v1); both answer 201 Created. Both run the same
/// handler, auth policy and validation, so they cannot drift (ADR-0004). It also maps <c>GET /api/v1/route/{id}</c>, which reads a stored
/// order back (FR-21). There is deliberately no GET under <c>/api/route</c>: the alias is POST-only (ADR-0004).
/// </summary>
internal static class RouteEndpoints
{
    /// <summary>Operation name of the canonical endpoint.</summary>
    public const string V1Name = "RouteOrderV1";

    /// <summary>Operation name of the alias endpoint.</summary>
    public const string AliasName = "RouteOrderAlias";

    /// <summary>Operation name of the read endpoint.</summary>
    public const string GetStoredOrderName = "GetStoredOrderV1";

    /// <summary>Maps the POST paths and the v1 GET.</summary>
    /// <param name="app">The endpoint route builder.</param>
    public static IEndpointRouteBuilder MapRouteEndpoints(this IEndpointRouteBuilder app)
    {
        Configure(app.MapGroup("/api/v1").MapPost("/route", HandleAsync), V1Name);
        Configure(app.MapPost("/api/route", HandleAsync), AliasName);

        app.MapGroup("/api/v1").MapGet("/route/{id}", GetAsync)
            .WithName(GetStoredOrderName)
            .WithTags("Routing")
            .RequireRouteCaller()
            // 200 is inferred from the handler's return type; the examples are added by RouteOpenApiTransformer.
            // No RouteEnvelopeMetadata: that is the POST-only 400 envelope. A GET has no body to validate.
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    // One place for the shared configuration, so the two paths always carry the same metadata.
    private static void Configure(RouteHandlerBuilder builder, string name)
        => builder
            .WithName(name)
            .WithTags("Routing")
            .RequireRouteCaller()
            .WithMetadata(RouteEnvelopeMetadata.Instance)
            // 201 is inferred from the handler's Created<RouteResponse> return type; the examples and the Location header are added by RouteOpenApiTransformer.
            .Produces<RouteResponse>(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

    private static async Task<Created<RouteResponse>> HandleAsync(RouteRequest request, ClaimsPrincipal user, IRouteOrderUseCase useCase, CancellationToken cancellationToken)
    {
        // The request already passed validation at the edge (FR-03); the command factory only guards invariants.
        var command = RouteOrderCommand.Create(
            request.OrderId!,
            request.CustomerZip!,
            request.MailOrder,
            [.. request.Items!.Select(item => new RouteOrderLine(item!.ProductCode!, item.Quantity!.Value))],
            // The experiment unit (AB-01): sub, then client_id, then azp; null falls back to the order ID. Never logged (NFR-11).
            CallerIdentity.Resolve(user));

        var response = await useCase.RouteAsync(command, cancellationToken);

        // SC-30: a valid POST creates a stored order, so it answers 201 Created. Location always points at the v1 path,
        // also for the alias, which is POST-only (ADR-0004, ADR-0007). The id is set by the use case after the save;
        // without it there is nothing to point at, which is a programming error and becomes a 500.
        var id = response.Id ?? throw new InvalidOperationException("The routing use case returned a response without a stored order id.");

        return TypedResults.Created($"/api/v1/route/{id}", response);
    }

    // FR-21, SC-31: one 404 for every case that is not "this caller's stored order" (unknown id, not a UUID, another
    // caller's order, an order stored without a caller). Same status, title and detail, so nothing reveals whether an id exists.
    private static async Task<Results<Ok<StoredOrderResponse>, ProblemHttpResult>> GetAsync(string id, ClaimsPrincipal user, IGetStoredOrderUseCase useCase, CancellationToken cancellationToken)
    {
        // The id is parsed here, after authentication, and not with a {id:guid} route constraint: a constraint would turn
        // an invalid id into a bodiless 404 before auth runs, so an anonymous call to /api/v1/route/abc would not get 401.
        // Only the canonical hyphenated form is accepted.
        if (!Guid.TryParseExact(id, "D", out var orderId))
        {
            return NotFound();
        }

        var order = await useCase.GetAsync(orderId, CallerIdentity.Resolve(user), cancellationToken);

        return order is null ? NotFound() : TypedResults.Ok(order);
    }

    private static ProblemHttpResult NotFound()
        => TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Order not found",
            detail: "No stored order was found for this id.");
}
