using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using OrderRouter.Api.Authentication;
using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.Application.Routing.Experiments;

namespace OrderRouter.Api.Routing;

/// <summary>
/// Maps <c>POST /api/v1/route</c> (canonical) and <c>POST /api/route</c> (alias pinned to v1). Both run the same
/// handler, auth policy and validation, so they cannot drift (ADR-0004).
/// </summary>
internal static class RouteEndpoints
{
    /// <summary>Operation name of the canonical endpoint.</summary>
    public const string V1Name = "RouteOrderV1";

    /// <summary>Operation name of the alias endpoint.</summary>
    public const string AliasName = "RouteOrderAlias";

    /// <summary>Maps both paths.</summary>
    /// <param name="app">The endpoint route builder.</param>
    public static IEndpointRouteBuilder MapRouteEndpoints(this IEndpointRouteBuilder app)
    {
        Configure(app.MapGroup("/api/v1").MapPost("/route", HandleAsync), V1Name);
        Configure(app.MapPost("/api/route", HandleAsync), AliasName);

        return app;
    }

    // One place for the shared configuration, so the two paths always carry the same metadata.
    private static void Configure(RouteHandlerBuilder builder, string name)
        => builder
            .WithName(name)
            .WithTags("Routing")
            .RequireRouteCaller()
            .WithMetadata(RouteEnvelopeMetadata.Instance)
            // 200 is inferred from the handler's Ok<RouteResponse> return type.
            .Produces<RouteResponse>(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

    private static async Task<Ok<RouteResponse>> HandleAsync(RouteRequest request, ClaimsPrincipal user, IRouteOrderUseCase useCase, CancellationToken cancellationToken)
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

        // 200 until OR-027 switches this to 201 + Location.
        return TypedResults.Ok(response);
    }
}
