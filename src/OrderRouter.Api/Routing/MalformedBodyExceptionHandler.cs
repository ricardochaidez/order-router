using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using OrderRouter.Application.Routing.Contracts;

namespace OrderRouter.Api.Routing;

/// <summary>
/// Turns a binding failure on a route endpoint (invalid JSON, empty body, wrong field type, wrong content type)
/// into the v1 <c>400</c> envelope with one error (SC-15, AC5). The parser's own message is never echoed (no
/// internals); only a sanitized JSON path is, so the caller can find the bad field.
/// </summary>
internal sealed partial class MalformedBodyExceptionHandler(IOptions<JsonOptions> jsonOptions) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var endpoint = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint;

        if (exception is not BadHttpRequestException || endpoint?.Metadata.GetMetadata<RouteEnvelopeMetadata>() is null)
        {
            return false;
        }

        var path = (exception.InnerException as JsonException)?.Path;
        var message = path is not null && SafePath().IsMatch(path) && path != "$"
            ? $"The request body is not valid JSON or has the wrong type near '{path[2..]}'."
            : "The request body is missing or is not valid JSON.";

        await RouteEnvelopeWriter.WriteBadRequestAsync(httpContext, RouteResponse.Invalid([message]), jsonOptions.Value.SerializerOptions);

        return true;
    }

    // Only echo plain member paths such as $.items[0].quantity.
    [GeneratedRegex(@"^\$(\.[A-Za-z0-9_]{1,64}(\[[0-9]{1,6}\])?){1,8}$")]
    private static partial Regex SafePath();
}
