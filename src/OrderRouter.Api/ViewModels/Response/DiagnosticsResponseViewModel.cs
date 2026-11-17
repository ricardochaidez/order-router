using System.Text.Json.Serialization;

namespace OrderRouter.Api.ViewModels.Response;

/// <summary>
/// The optional <c>diagnostics</c> object (AB-05, OQ-08, ADR-0004). It holds only the experiment arms and the search
/// mode, as stored. It never holds the caller ID or its hash, the config version or targeting rules.
/// </summary>
/// <example>
/// <code>
/// { "experiment_arms": { "exp1_consolidation_vs_quality": { "arm": "control", "reason": "disabled" } }, "search_mode": "exact" }
/// </code>
/// </example>
public sealed record DiagnosticsResponseViewModel
{
    /// <summary>Arm and reason per experiment, keyed by the snake_case experiment name. Always present.</summary>
    public IReadOnlyDictionary<string, ExperimentDiagnosticResponseViewModel> ExperimentArms
    {
        get => field;
        init => field = value ?? new Dictionary<string, ExperimentDiagnosticResponseViewModel>();
    } = new Dictionary<string, ExperimentDiagnosticResponseViewModel>();

    /// <summary><c>exact</c> or <c>greedy</c>. Omitted when no search ran.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SearchMode { get; init; }
}
