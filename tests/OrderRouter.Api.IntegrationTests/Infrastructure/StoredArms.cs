using System.Text.Json;
using Npgsql;

namespace OrderRouter.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Reads the experiment arms of a stored order straight from the database. The API does not return them (clients must
/// not know which arm they got), so analysis and harness checks read the stored record instead.
/// </summary>
internal static class StoredArms
{
    /// <summary>The <c>experiment_arms</c> object stored for the order with <paramref name="id"/> (the <c>id</c> in the 201 body).</summary>
    public static async Task<JsonElement> ReadAsync(string connectionString, JsonElement responseBody, CancellationToken ct)
    {
        var id = responseBody.GetProperty("id").GetGuid();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT experiment_arms::text FROM routing_orders WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        var text = (string?)await command.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException($"No stored order {id}.");

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Asserts the stored arms of an order equal <paramref name="combination"/>.</summary>
    public static async Task AssertAsync(string connectionString, JsonElement responseBody, ExperimentCombination combination, CancellationToken ct)
    {
        var arms = await ReadAsync(connectionString, responseBody, ct);
        Assert.Equal(ExperimentCombination.ArmName(combination.Exp1), arms.GetProperty("exp1_consolidation_vs_quality").GetProperty("arm").GetString());
        Assert.Equal(ExperimentCombination.ArmName(combination.Exp2), arms.GetProperty("exp2_unrated_suppliers").GetProperty("arm").GetString());
        Assert.Equal(ExperimentCombination.ArmName(combination.Exp3), arms.GetProperty("exp3_partial_routing").GetProperty("arm").GetString());
        Assert.Equal(ExperimentCombination.ArmName(combination.Exp4), arms.GetProperty("exp4_tie_breaking").GetProperty("arm").GetString());
    }
}
