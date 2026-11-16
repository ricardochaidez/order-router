using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderRouter.Api.Authentication;

namespace OrderRouter.IntegrationTests.Authentication;

public class SecretLoggingTests(ReferenceDataFixture fixture)
{
    private const string configuredKey = "configured-secret-dev-key";

    private const string wrongKey = "wrong-secret-dev-key";

    [Fact]
    public async Task Requests_WithTokensAndKeys_NeverLogTheirValues()
    {
        var provider = new CapturingProvider();
        await using var factory = AuthApiFactory.Create(
            fixture.ConnectionString,
            devApiKey: configuredKey,
            configureLogging: logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.Services.AddSingleton<ILoggerProvider>(provider);
            });
        using var client = factory.CreateClient();
        var tokens = new[] { TestJwt.Valid(), TestJwt.Expired(), TestJwt.BadSignature(), TestJwt.WrongAudience() };

        foreach (var token in tokens)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, AuthApiFactory.ProbePath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        foreach (var key in new[] { configuredKey, wrongKey })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, AuthApiFactory.ProbePath);
            request.Headers.Add(DevApiKeyDefaults.HeaderName, key);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        var logged = string.Join('\n', provider.Messages);
        Assert.NotEmpty(provider.Messages);
        Assert.All(tokens, token => Assert.DoesNotContain(token, logged, StringComparison.Ordinal));
        Assert.DoesNotContain(configuredKey, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(wrongKey, logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_DevKeyConfiguredInProduction_LogsWarningWithoutTheValue()
    {
        var provider = new CapturingProvider();
        await using var factory = AuthApiFactory.Create(
            fixture.ConnectionString,
            "Production",
            configuredKey,
            configureLogging: logging => logging.Services.AddSingleton<ILoggerProvider>(provider));

        using var client = factory.CreateClient();

        Assert.Contains(provider.Messages, message => message.Contains("Authentication:DevApiKey", StringComparison.Ordinal) && message.Contains("ignored", StringComparison.Ordinal));
        Assert.DoesNotContain(configuredKey, string.Join('\n', provider.Messages), StringComparison.Ordinal);
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Enqueue(formatter(state, exception) + exception);
        }
    }
}
