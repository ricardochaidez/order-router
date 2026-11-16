using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderRouter.Application.Routing;
using OrderRouter.Application.Routing.Contracts;
using OrderRouter.IntegrationTests.Authentication;

namespace OrderRouter.IntegrationTests.Routing;

/// <summary>
/// Builds the real API host (JWT test setup from <see cref="AuthApiFactory"/>) with the routing use case replaced
/// by a fake, so the endpoint contract is tested without routing logic, or left real for end-to-end tests. OR-023 reuses this.
/// </summary>
internal static class RouteApiFactory
{
    /// <summary>The two paths that must behave identically (ADR-0004).</summary>
    public static TheoryData<string> Paths => ["/api/v1/route", "/api/route"];

    /// <summary>Creates the host. Pass <paramref name="useCase"/> as <c>null</c> to keep the real routing use case (OR-015).</summary>
    public static WebApplicationFactory<Program> Create(string connectionString, IRouteOrderUseCase? useCase)
        => AuthApiFactory.Create(connectionString, configureServices: services =>
        {
            if (useCase is not null)
            {
                services.RemoveAll<IRouteOrderUseCase>();
                services.AddSingleton(useCase);
            }
        });

    /// <summary>An authenticated POST with a raw JSON body.</summary>
    public static HttpRequestMessage Post(string path, string? body, bool authenticated = true, string contentType = "application/json", string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, contentType);
        }

        if (authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? TestJwt.Valid());
        }

        return request;
    }

    /// <summary>A valid request body (requirements §9.1) with the ignored extra fields.</summary>
    public const string ValidBody = """
        { "order_id": "ORD-EXAMPLE", "customer_zip": "10015", "mail_order": false, "priority": "high", "notes": "ignore me",
          "items": [ { "product_code": "WC-STD-001", "quantity": 1 }, { "product_code": "OX-PORT-024", "quantity": 1 } ] }
        """;
}

/// <summary>Records the commands it receives and returns a fixed response.</summary>
internal sealed class RecordingRouteUseCase(RouteResponse? response = null) : IRouteOrderUseCase
{
    private readonly List<RouteOrderCommand> commands = [];

    public IReadOnlyList<RouteOrderCommand> Commands => commands;

    public Task<RouteResponse> RouteAsync(RouteOrderCommand command, CancellationToken cancellationToken)
    {
        commands.Add(command);

        return Task.FromResult(response ?? new RouteResponse { OrderId = command.OrderId, Status = RoutingStatus.NotRouted });
    }
}

/// <summary>Throws like an unexpected fault would, so the 500 contract can be checked.</summary>
internal sealed class ThrowingRouteUseCase : IRouteOrderUseCase
{
    public Task<RouteResponse> RouteAsync(RouteOrderCommand command, CancellationToken cancellationToken)
        => throw new NotSupportedException("secret internal detail");
}
