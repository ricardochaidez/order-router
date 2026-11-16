using System.Text.Json.Serialization;

namespace OrderRouter.Application.Routing.Contracts;

/// <summary>The arm one experiment gave an order, and why (AB-04).</summary>
/// <param name="Arm"><c>control</c> or <c>treatment</c>.</param>
/// <param name="Reason">Why the order got that arm, for example <c>disabled</c> or <c>bucketed</c>.</param>
public sealed record ExperimentDiagnostic(string Arm, string Reason);

/// <summary>
/// The optional <c>diagnostics</c> object (AB-05, OQ-08, ADR-0004). It holds only the experiment arms and the search
/// mode, as stored. It never holds the caller ID or its hash, the config version or targeting rules.
/// </summary>
/// <example>
/// <code>
/// { "experiment_arms": { "exp1_consolidation_vs_quality": { "arm": "control", "reason": "disabled" } }, "search_mode": "exact" }
/// </code>
/// </example>
public sealed record Diagnostics
{
    /// <summary>Arm and reason per experiment, keyed by the snake_case experiment name. Always present.</summary>
    public IReadOnlyDictionary<string, ExperimentDiagnostic> ExperimentArms
    {
        get => field;
        init => field = value ?? new Dictionary<string, ExperimentDiagnostic>();
    } = new Dictionary<string, ExperimentDiagnostic>();

    /// <summary><c>exact</c> or <c>greedy</c>. Omitted when no search ran.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SearchMode { get; init; }
}
