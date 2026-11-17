using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OrderRouter.Api.IntegrationTests.Infrastructure;

/// <summary>Records the text of every SQL command a context executes, so tests can prove what was sent (AC4).</summary>
public sealed class CapturingCommandInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> commands = new();

    /// <summary>The commands executed so far, in order.</summary>
    public IReadOnlyList<string> Commands => [.. commands];

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken ct = default)
    {
        commands.Enqueue(command.CommandText);

        return base.ReaderExecutingAsync(command, eventData, result, ct);
    }

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        commands.Enqueue(command.CommandText);

        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        commands.Enqueue(command.CommandText);

        return base.NonQueryExecutingAsync(command, eventData, result, ct);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken ct = default)
    {
        commands.Enqueue(command.CommandText);

        return base.ScalarExecutingAsync(command, eventData, result, ct);
    }
}
