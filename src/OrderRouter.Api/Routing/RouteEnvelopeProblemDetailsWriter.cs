using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using OrderRouter.Application.Routing.Contracts;

namespace OrderRouter.Api.Routing;

/// <summary>
/// Rewrites the framework's validation <c>400</c> into the v1 envelope for endpoints tagged with
/// <see cref="RouteEnvelopeMetadata"/> (SC-15). It must be registered before the default writer.
/// </summary>
internal sealed class RouteEnvelopeProblemDetailsWriter(IOptions<JsonOptions> jsonOptions) : Microsoft.AspNetCore.Http.IProblemDetailsWriter
{
    /// <inheritdoc />
    public bool CanWrite(ProblemDetailsContext context)
        => context.ProblemDetails is Microsoft.AspNetCore.Http.HttpValidationProblemDetails
           && context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RouteEnvelopeMetadata>() is not null;

    /// <inheritdoc />
    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        var details = (Microsoft.AspNetCore.Http.HttpValidationProblemDetails)context.ProblemDetails;
        var body = RouteResponse.Invalid(ValidationErrorFormatter.Format(details.Errors));

        return RouteEnvelopeWriter.WriteBadRequestAsync(context.HttpContext, body, jsonOptions.Value.SerializerOptions);
    }
}

/// <summary>Writes the 400 envelope. Shared by the validation writer and the malformed-body handler.</summary>
internal static class RouteEnvelopeWriter
{
    /// <summary>Writes <paramref name="body"/> as JSON with status 400.</summary>
    public static async ValueTask WriteBadRequestAsync(HttpContext context, RouteResponse body, System.Text.Json.JsonSerializerOptions options)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(body, options, contentType: "application/json", cancellationToken: context.RequestAborted);
    }
}
