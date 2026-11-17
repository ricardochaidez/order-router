namespace OrderRouter.Domain.Entities.Experiments;

/// <summary>Which behaviour an experiment applies to a request (ADR-0005, AB-03).</summary>
public enum ExperimentArm
{
    /// <summary>The behaviour in the written spec.</summary>
    Control,

    /// <summary>The behaviour being tested.</summary>
    Treatment,
}
