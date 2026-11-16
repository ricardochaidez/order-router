namespace OrderRouter.Api.Routing;

/// <summary>
/// Endpoint marker: failures on this endpoint (validation, malformed body) are written in the v1 envelope
/// (SC-15) instead of plain ProblemDetails. Other endpoints keep standard ProblemDetails.
/// </summary>
internal sealed class RouteEnvelopeMetadata
{
    /// <summary>The single shared instance.</summary>
    public static RouteEnvelopeMetadata Instance { get; } = new();
}
