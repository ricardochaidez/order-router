using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OrderRouter.Api.IntegrationTests.Scenarios.V1;

/// <summary>
/// Shared helper for the OR-023 acceptance suite: posts an order to the real host (real routing use case, shared
/// seeded database), reads the JSON, and follows <c>Location</c>. It reuses <see cref="RouteApiFactory"/>
/// and the test JWTs, so there is one way to build the host.
/// </summary>
internal sealed class AcceptanceApi(ReferenceDataFixture fixture)
{
    /// <summary>What a request returned, with the body kept as text and, when it is a JSON object, as JSON.</summary>
    internal sealed record Reply(HttpResponseMessage Response, string Text, JsonElement Json)
    {
        public HttpStatusCode Status => Response.StatusCode;

        public string? Location => Response.Headers.Location?.OriginalString;

        /// <summary>The supplier ids of the shipments, in response order.</summary>
        public string[] SupplierIds => [.. Json.GetProperty("routing").EnumerateArray().Select(group => group.GetProperty("supplier_id").GetString()!)];

        /// <summary>Every routed item across all shipments.</summary>
        public JsonElement[] Items => [.. Json.GetProperty("routing").EnumerateArray().SelectMany(group => group.GetProperty("items").EnumerateArray())];
    }

    /// <summary>A unique <c>order_id</c>, because the shared database accumulates stored orders and tests run in parallel.</summary>
    public static string NewOrderId() => $"ORD-{Guid.NewGuid():N}";

    /// <summary>Builds a request body for <paramref name="orderId"/>.</summary>
    public static string Body(string orderId, string zip, bool mailOrder, params (string Code, int Quantity)[] items)
        => $$"""
            { "order_id": "{{orderId}}", "customer_zip": "{{zip}}", "mail_order": {{(mailOrder ? "true" : "false")}},
              "items": [ {{string.Join(", ", items.Select(item => $$"""{ "product_code": "{{item.Code}}", "quantity": {{item.Quantity}} }"""))}} ] }
            """;

    /// <summary>Creates the real host over the shared seeded database.</summary>
    public WebApplicationFactory<Program> CreateFactory() => RouteApiFactory.Create(fixture.ConnectionString, useCase: null);

    /// <summary>Sends any request through <paramref name="factory"/> and reads the reply.</summary>
    public static async Task<Reply> SendAsync(WebApplicationFactory<Program> factory, HttpRequestMessage request)
    {
        using var client = factory.CreateClient();
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = text.Length > 0 && text[0] == '{' ? JsonDocument.Parse(text).RootElement.Clone() : default;

        return new Reply(response, text, json);
    }

    /// <summary>Posts <paramref name="body"/> to <paramref name="path"/> as a caller (a valid token unless one is given).</summary>
    public static Task<Reply> PostAsync(WebApplicationFactory<Program> factory, string path, string? body, string? token = null)
        => SendAsync(factory, RouteApiFactory.Post(path, body, token: token));

    /// <summary>Posts and asserts 201, so a test that expects a created order fails with the body on any other status.</summary>
    public static async Task<Reply> PostCreatedAsync(WebApplicationFactory<Program> factory, string path, string body, string? token = null)
    {
        var reply = await PostAsync(factory, path, body, token);

        Assert.True(reply.Status == HttpStatusCode.Created, $"Expected 201 but got {(int)reply.Status}: {reply.Text}");
        Assert.Equal(reply.Json.GetProperty("status").GetString() == "routed", reply.Json.GetProperty("feasible").GetBoolean());

        return reply;
    }

    /// <summary>Follows a <c>Location</c> with a GET as the same caller.</summary>
    public static Task<Reply> GetAsync(WebApplicationFactory<Program> factory, string location, string? token = null)
        => SendAsync(factory, RouteApiFactory.Get(location, token: token));
}
