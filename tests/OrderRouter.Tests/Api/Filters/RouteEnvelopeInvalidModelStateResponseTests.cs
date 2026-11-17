using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using OrderRouter.Api.Filters;
using OrderRouter.Api.ViewModels.Response;

namespace OrderRouter.Tests.Api.Filters;

public class RouteEnvelopeInvalidModelStateResponseTests
{
    private static ActionContext Context(bool envelope, Action<ModelStateDictionary> addErrors)
    {
        var descriptor = new ActionDescriptor
        {
            EndpointMetadata = envelope ? [new RouteEnvelopeAttribute()] : [],
            Parameters = [new ParameterDescriptor { Name = "request", BindingInfo = new BindingInfo { BindingSource = BindingSource.Body } }],
        };
        var context = new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor);
        addErrors(context.ModelState);

        return context;
    }

    private static IReadOnlyList<string> Errors(IActionResult result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);

        return Assert.IsType<RouteResponseViewModel>(badRequest.Value).Errors;
    }

    [Fact]
    public void Create_ValidationErrors_ListsEverySnakeCaseErrorInRequestOrder()
    {
        var context = Context(envelope: true, state =>
        {
            state.AddModelError("Items[0].Quantity", "must be at least 1.");
            state.AddModelError("CustomerZip", "is required.");
        });

        var errors = Errors(RouteEnvelopeInvalidModelStateResponse.Create(context));

        Assert.Equal(["customer_zip is required.", "items[0].quantity must be at least 1."], errors);
    }

    [Fact]
    public void Create_WrongTypeAtPlainJsonPath_NamesThePathWithoutParserMessage()
    {
        var context = Context(envelope: true, state =>
        {
            state.AddModelError("$.items[0].quantity", "System.Text.Json internal detail. LineNumber: 0");
            state.AddModelError("request", "The request field is required.");
        });

        var error = Assert.Single(Errors(RouteEnvelopeInvalidModelStateResponse.Create(context)));

        Assert.Equal("The request body is not valid JSON or has the wrong type near 'items[0].quantity'.", error);
    }

    [Theory]
    [InlineData("$")]
    [InlineData("")]
    [InlineData("request")]
    [InlineData("$.items['odd key']")]
    public void Create_UnreadableBody_ReturnsOneGenericError(string key)
    {
        var context = Context(envelope: true, state => state.AddModelError(key, new JsonException("parser internals"), new EmptyModelMetadataProvider().GetMetadataForType(typeof(object))));

        var error = Assert.Single(Errors(RouteEnvelopeInvalidModelStateResponse.Create(context)));

        Assert.Equal("The request body is missing or is not valid JSON.", error);
    }

    [Fact]
    public void Configure_UntaggedAction_UsesTheFrameworkFactory()
    {
        var fallbackResult = new BadRequestResult();
        var options = new ApiBehaviorOptions { InvalidModelStateResponseFactory = _ => fallbackResult };
        RouteEnvelopeInvalidModelStateResponse.Configure(options);

        var result = options.InvalidModelStateResponseFactory(Context(envelope: false, state => state.AddModelError("Id", "bad")));

        Assert.Same(fallbackResult, result);
    }

    [Fact]
    public void Configure_TaggedAction_UsesTheEnvelope()
    {
        var options = new ApiBehaviorOptions { InvalidModelStateResponseFactory = _ => new BadRequestResult() };
        RouteEnvelopeInvalidModelStateResponse.Configure(options);

        var result = options.InvalidModelStateResponseFactory(Context(envelope: true, state => state.AddModelError("OrderId", "is required.")));

        Assert.Equal(["order_id is required."], Errors(result));
    }
}
