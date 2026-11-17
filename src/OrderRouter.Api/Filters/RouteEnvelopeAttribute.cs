using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using OrderRouter.Api.OpenApi;

namespace OrderRouter.Api.Filters;

/// <summary>
/// Action marker: request failures on this action (validation, malformed body) are written in the v1 envelope
/// (SC-15) by <see cref="RouteEnvelopeInvalidModelStateResponse"/> instead of plain ProblemDetails. Other actions keep
/// standard ProblemDetails. It is also how <see cref="RouteOpenApiTransformer"/> finds the route operations.
/// </summary>
/// <remarks>
/// It is also a filter for one case MVC would otherwise answer differently from the v1 contract: a POST with no body at
/// all. Without a Content-Type MVC records an <see cref="UnsupportedContentTypeException"/>, and its built-in filter
/// turns that into a <c>415</c>. A missing body is a missing required field, so it is answered with the <c>400</c>
/// envelope instead. A body that is present but not JSON still gets the <c>415</c> (RFC 9110).
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class RouteEnvelopeAttribute : Attribute, IActionFilter, IOrderedFilter
{
    /// <summary>Runs just before MVC's <c>UnsupportedContentTypeFilter</c> (order -3000).</summary>
    public int Order => -3001;

    /// <inheritdoc />
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;
        var hasNoBody = request.ContentLength is 0
            || context.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>() is { CanHaveBody: false };

        if (hasNoBody && context.ModelState.Values.Any(entry => entry.Errors.Any(error => error.Exception is UnsupportedContentTypeException)))
        {
            context.Result = RouteEnvelopeInvalidModelStateResponse.Create(context);
        }
    }

    /// <inheritdoc />
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
