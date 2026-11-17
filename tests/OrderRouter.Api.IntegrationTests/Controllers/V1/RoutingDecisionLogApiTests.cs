using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderRouter.Domain.Entities.Routing;
using OrderRouter.Domain.Interfaces;
using OrderRouter.Infrastructure.Providers;
using OrderRouter.Infrastructure;

namespace OrderRouter.Api.IntegrationTests.Controllers.V1;

/// <summary>
/// OR-021 acceptance: the real host writes one <c>RoutingDecisionMade</c> event per stored order, after the save, with
/// the stored id and only the caller's hash (AB-04, ADR-0005, NFR-11). Every test uses its own <c>order_id</c> and reads
/// only the events of that order, because the shared host logs and the shared database are used in parallel.
/// </summary>
public class RoutingDecisionLogApiTests(ReferenceDataFixture fixture)
{
    private const string eventName = "RoutingDecisionMade";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string NewOrderId() => $"ORD-{Guid.NewGuid():N}";

    private static string Routable(string orderId)
        => $$"""
            { "order_id": "{{orderId}}", "customer_zip": "10015", "mail_order": false,
              "items": [ { "product_code": "WC-STD-001", "quantity": 1 }, { "product_code": "OX-PORT-024", "quantity": 1 } ] }
            """;

    // ORD-003: the mail order whose supplier is picked by the random tie-break (SUP-023 or SUP-065, NFR-08b).
    private static string Tied(string orderId)
        => $$"""
            { "order_id": "{{orderId}}", "customer_zip": "02130", "mail_order": true,
              "items": [ { "product_code": "CP-STD-031", "quantity": 1 }, { "product_code": "CP-MSK-FF-035", "quantity": 2 },
                         { "product_code": "NB-COMP-039", "quantity": 1 } ] }
            """;

    private static WebApplicationFactory<Program> CreateHost(string connectionString, CapturingProvider provider, Action<IServiceCollection>? configureServices = null)
        => AuthApiFactory.Create(
            connectionString,
            configureLogging: logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.Services.AddSingleton<ILoggerProvider>(provider);
            },
            configureServices: configureServices);

    private static async Task<(HttpResponseMessage Response, JsonElement Json)> PostAsync(WebApplicationFactory<Program> factory, string body, string? token = null, bool authenticated = true)
    {
        using var client = factory.CreateClient();
        var response = await client.SendAsync(RouteApiFactory.Post("/api/v1/route", body, authenticated, token: token), Token);
        var text = await response.Content.ReadAsStringAsync(Token);

        return (response, text.StartsWith('{') ? JsonDocument.Parse(text).RootElement.Clone() : default);
    }

    // AC1
    [Fact]
    public async Task Post_ValidOrder_WritesOneEventWithTheStoredIdAndEveryField()
    {
        var provider = new CapturingProvider();
        var orderId = NewOrderId();
        await using var factory = CreateHost(fixture.ConnectionString, provider);

        var (response, json) = await PostAsync(factory, Routable(orderId), TestJwt.ForSubject("user-123"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var logged = Assert.Single(provider.EventsFor(orderId));
        Assert.Equal(json.GetProperty("id").GetString(), logged["id"]?.ToString());
        Assert.Equal(
            [
                "id", "order_id", "caller_hash", "exp1_arm", "exp1_reason", "exp2_arm", "exp2_reason", "exp3_arm", "exp3_reason",
                "chosen_suppliers", "tied_candidates", "tie_pick", "shipment_count", "plan_quality", "unrated_chosen", "unfulfilled_count",
                "search_mode", "config_version", "customer_zip",
            ],
            logged.Keys.Where(key => key != "{OriginalFormat}"));
        Assert.Equal("SUP-0810:local", logged["chosen_suppliers"]);
        Assert.Equal(1, logged["shipment_count"]);
        Assert.Equal(0, logged["unfulfilled_count"]);
        Assert.Equal("exact", logged["search_mode"]);
        Assert.Equal("10015", logged["customer_zip"]);
        Assert.Equal("control", logged["exp1_arm"]);
        Assert.Equal("disabled", logged["exp1_reason"]);
        Assert.Null(logged["tied_candidates"]);
        Assert.False(string.IsNullOrWhiteSpace(logged["config_version"]?.ToString()));
    }

    // AC2: whatever the random pick, the event lists both tied candidates and the pick that the response shows.
    [Fact]
    public async Task Post_OrderWithATieBreak_ListsTheTiedCandidatesAndThePick()
    {
        var provider = new CapturingProvider();
        var orderId = NewOrderId();
        await using var factory = CreateHost(fixture.ConnectionString, provider);

        var (_, json) = await PostAsync(factory, Tied(orderId));

        var logged = Assert.Single(provider.EventsFor(orderId));
        var chosenSupplier = json.GetProperty("routing")[0].GetProperty("supplier_id").GetString();
        Assert.Equal(["SUP-023", "SUP-065"], logged["tied_candidates"]!.ToString()!.Split(',').Order());
        Assert.Equal(chosenSupplier, logged["tie_pick"]);
    }

    // AC3: the caller ID appears nowhere in anything this host logged; only its hash does.
    [Fact]
    public async Task Post_CallerUser123_NeverAppearsInAnyLogAndOnlyTheHashIsLogged()
    {
        var provider = new CapturingProvider();
        var orderId = NewOrderId();
        await using var factory = CreateHost(fixture.ConnectionString, provider);

        await PostAsync(factory, Routable(orderId), TestJwt.ForSubject("user-123"));

        var logged = Assert.Single(provider.EventsFor(orderId));
        Assert.Equal(factory.Services.GetRequiredService<ICallerHasher>().Hash("user-123"), logged["caller_hash"]);
        Assert.DoesNotContain("user-123", provider.Everything(), StringComparison.Ordinal);
    }

    // AC4
    [Fact]
    public async Task Post_InvalidRequest_Returns400AndWritesNoEvent()
    {
        var provider = new CapturingProvider();
        var orderId = NewOrderId();
        await using var factory = CreateHost(fixture.ConnectionString, provider);

        var (response, _) = await PostAsync(factory, $$"""{ "order_id": "{{orderId}}", "customer_zip": "123", "items": [] }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(provider.Events());
    }

    // AC4
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_NoOrBadCredentials_Returns401AndWritesNoEvent(bool badToken)
    {
        var provider = new CapturingProvider();
        await using var factory = CreateHost(fixture.ConnectionString, provider);

        var (response, _) = await PostAsync(factory, Routable(NewOrderId()), token: badToken ? TestJwt.BadSignature() : null, authenticated: badToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(provider.Events());
    }

    // AC5: the database rejects the insert for real (a check constraint), so the 500 and the rollback are real.
    [Fact]
    public async Task Post_DatabaseRejectsTheInsert_Returns500AndWritesNoEvent()
    {
        var provider = new CapturingProvider();
        var orderId = NewOrderId();
        await using var factory = CreateHost(fixture.ConnectionString, provider, services =>
        {
            services.RemoveAll<IRoutingOrderStore>();
            services.AddScoped<IRoutingOrderStore>(serviceProvider => new RejectingStore(new RoutingOrderStore(serviceProvider.GetRequiredService<OrderRouterDataContext>())));
        });

        var (response, _) = await PostAsync(factory, Routable(orderId));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(provider.Events());
        Assert.Equal(["0"], await fixture.QueryAsync($"SELECT count(*) FROM routing_orders WHERE order_id = '{orderId}'", Token));
    }

    // SC-29: a retry is a new stored order, so it gets its own event with its own id.
    [Fact]
    public async Task Post_SameBodyTwice_WritesTwoEventsWithDifferentIds()
    {
        var provider = new CapturingProvider();
        var orderId = NewOrderId();
        await using var factory = CreateHost(fixture.ConnectionString, provider);

        await PostAsync(factory, Routable(orderId));
        await PostAsync(factory, Routable(orderId));

        var events = provider.EventsFor(orderId);
        Assert.Equal(2, events.Count);
        Assert.NotEqual(events[0]["id"], events[1]["id"]);
    }

    // AB-04: the event is also written for an order that is not routed.
    [Fact]
    public async Task Post_NotRoutedOrder_StillWritesAnEventWithTheUnfulfilledCount()
    {
        var provider = new CapturingProvider();
        var orderId = NewOrderId();
        await using var factory = CreateHost(fixture.ConnectionString, provider);

        var (response, _) = await PostAsync(factory, $$"""
            { "order_id": "{{orderId}}", "customer_zip": "10015", "mail_order": false,
              "items": [ { "product_code": "XX-UNKNOWN-999", "quantity": 1 } ] }
            """);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var logged = Assert.Single(provider.EventsFor(orderId));
        Assert.Equal(1, logged["unfulfilled_count"]);
        Assert.Equal(0, logged["shipment_count"]);
        Assert.Equal(string.Empty, logged["chosen_suppliers"]);
        Assert.Null(logged["search_mode"]);
    }

    // Makes the database reject the insert by sending a quantity that violates ck_routing_order_items_quantity.
    private sealed class RejectingStore(IRoutingOrderStore inner) : IRoutingOrderStore
    {
        public Task SaveAsync(StoredRoutingOrder order, CancellationToken ct)
            => inner.SaveAsync(order with { Items = [.. order.Items.Select((item, index) => index == 1 ? item with { Quantity = 0 } : item)] }, ct);
    }

    // Keeps every log entry of the host with its structured properties, so tests can read fields instead of parsing text.
    private sealed class CapturingProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(string Category, EventId EventId, string Message, Dictionary<string, object?> Properties)> entries = new();

        public ILogger CreateLogger(string categoryName) => new Capturing(categoryName, entries);

        public List<Dictionary<string, object?>> Events()
            => [.. entries.Where(entry => entry.EventId.Name == eventName).Select(entry => entry.Properties)];

        public List<Dictionary<string, object?>> EventsFor(string orderId)
            => [.. Events().Where(properties => Equals(properties["order_id"], orderId))];

        public string Everything()
            => string.Join('\n', entries.Select(entry => entry.Message + " " + string.Join(' ', entry.Properties.Values)));

        public void Dispose()
        {
        }

        private sealed class Capturing(string category, ConcurrentQueue<(string, EventId, string, Dictionary<string, object?>)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value) : [];
                entries.Enqueue((category, eventId, formatter(state, exception), properties));
            }
        }
    }
}
