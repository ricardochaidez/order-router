using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;
using OrderRouter.Api.ViewModels.Response;

namespace OrderRouter.Api.Filters;

/// <summary>
/// Writes the automatic <see cref="ApiControllerAttribute"/> <c>400</c> in the v1 envelope for actions tagged with
/// <see cref="RouteEnvelopeAttribute"/> (SC-15, AC1–AC7). MVC records both data-annotation failures and body binding
/// failures (invalid JSON, empty body, wrong field type) in <see cref="ModelStateDictionary"/>, so this one factory
/// covers both. Untagged actions keep the framework's ValidationProblemDetails.
/// </summary>
internal static partial class RouteEnvelopeInvalidModelStateResponse
{
    /// <summary>Wraps the configured factory, so untagged actions fall through to it unchanged.</summary>
    /// <param name="options">The options whose factory is replaced. The framework default must already be set.</param>
    public static void Configure(ApiBehaviorOptions options)
    {
        var fallback = options.InvalidModelStateResponseFactory;

        options.InvalidModelStateResponseFactory = context
            => context.ActionDescriptor.EndpointMetadata.OfType<RouteEnvelopeAttribute>().Any()
                ? Create(context)
                : fallback(context);
    }

    /// <summary>
    /// Builds the envelope. A body that could not be read at all yields exactly one error, and the parser's own message
    /// is never echoed (no internals): only a sanitized JSON path is, so the caller can find the bad field. Otherwise
    /// every validation error is listed in request order.
    /// </summary>
    /// <param name="context">The failed action's context.</param>
    public static IActionResult Create(ActionContext context)
    {
        IReadOnlyList<string> errors = BodyErrorOrNull(context) is { } bodyError
            ? [bodyError]
            : ValidationErrorFormatter.Format(context.ModelState
                .Where(entry => entry.Value is { Errors.Count: > 0 })
                .Select(entry => KeyValuePair.Create(entry.Key, entry.Value!.Errors.Select(error => error.ErrorMessage).ToArray())));

        return new BadRequestObjectResult(RouteResponseViewModel.Invalid(errors)) { ContentTypes = { "application/json" } };
    }

    // A binding failure shows up as: an error carrying the formatter's exception, a key that is a JSON path ("$...", from
    // System.Text.Json), the empty key ("A non-empty request body is required."), or the body parameter itself (a JSON
    // null body fails its implicit [Required]). Property validation errors use member paths such as Items[0].Quantity.
    private static string? BodyErrorOrNull(ActionContext context)
    {
        var bodyParameter = context.ActionDescriptor.Parameters
            .FirstOrDefault(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body)?.Name;

        var failed = context.ModelState.Where(entry => entry.Value is { Errors.Count: > 0 }).ToArray();
        var isBodyFailure = failed.Any(entry =>
            entry.Key.Length == 0
            || entry.Key.StartsWith('$')
            || entry.Key == bodyParameter
            || entry.Value!.Errors.Any(error => error.Exception is not null));

        if (!isBodyFailure)
        {
            return null;
        }

        var path = failed.Select(entry => entry.Key).FirstOrDefault(key => key != "$" && SafePath().IsMatch(key));

        return path is not null
            ? $"The request body is not valid JSON or has the wrong type near '{path[2..]}'."
            : "The request body is missing or is not valid JSON.";
    }

    // Only echo plain member paths such as $.items[0].quantity.
    [GeneratedRegex(@"^\$(\.[A-Za-z0-9_]{1,64}(\[[0-9]{1,6}\])?){1,8}$")]
    private static partial Regex SafePath();
}
