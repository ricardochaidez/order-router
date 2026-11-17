using OrderRouter.Domain.Entities.ReferenceData;
using OrderRouter.Domain.Helpers;
using OrderRouter.Domain.Models;

namespace OrderRouter.Domain.Entities.Experiments;

/// <summary>
/// One experiment's switch, traffic share, salt and targeting. Evaluating it is a pure function of the request
/// (ADR-0005 flowchart): disabled, then targeting, then the salted bucket.
/// </summary>
public sealed record ExperimentDefinition
{
    /// <summary>Creates a definition, reporting every invalid field in one exception.</summary>
    /// <param name="id">The experiment key, part of the bucket input so each experiment is independent (AB-01).</param>
    /// <param name="enabled">Whether the experiment runs; off means everyone is on control (AB-03).</param>
    /// <param name="treatmentPercentage">Share of targeted units in treatment, 0 to 100.</param>
    /// <param name="salt">Non-empty salt; changing it reshuffles the units.</param>
    /// <param name="targeting">Who and where the experiment applies to.</param>
    /// <exception cref="DomainValidationException">One or more values are invalid.</exception>
    public ExperimentDefinition(string id, bool enabled, int treatmentPercentage, string salt, ExperimentTargeting targeting)
    {
        ArgumentNullException.ThrowIfNull(targeting);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(id))
        {
            errors.Add("experiment id must not be empty");
        }

        if (treatmentPercentage is < 0 or > 100)
        {
            errors.Add($"treatment percentage must be between 0 and 100 inclusive (received {treatmentPercentage})");
        }

        if (string.IsNullOrWhiteSpace(salt))
        {
            errors.Add("salt must not be empty");
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException($"Invalid experiment definition: {string.Join("; ", errors)}.");
        }

        Id = id;
        Enabled = enabled;
        TreatmentPercentage = treatmentPercentage;
        Salt = salt;
        Targeting = targeting;
    }

    /// <summary>The experiment key.</summary>
    public string Id { get; }

    /// <summary>Whether the experiment runs.</summary>
    public bool Enabled { get; }

    /// <summary>Share of targeted units in treatment, 0 to 100.</summary>
    public int TreatmentPercentage { get; }

    /// <summary>The bucket salt.</summary>
    public string Salt { get; }

    /// <summary>Who and where the experiment applies to.</summary>
    public ExperimentTargeting Targeting { get; }

    /// <summary>A disabled experiment: the fail-safe default when configuration is missing (AB-03).</summary>
    /// <param name="id">The experiment key.</param>
    /// <param name="salt">The salt to keep, so enabling it later needs no other edit.</param>
    public static ExperimentDefinition Disabled(string id, string salt) => new(id, false, 0, salt, ExperimentTargeting.Everyone);

    /// <summary>Decides the arm and the reason for one request.</summary>
    /// <param name="callerId">The authenticated caller, or <c>null</c> (then the order ID is the unit, AB-01).</param>
    /// <param name="zip">The customer ZIP.</param>
    /// <param name="orderId">The order ID.</param>
    public (ExperimentArm Arm, ExposureReason Reason) Evaluate(string? callerId, ZipCode zip, string orderId)
    {
        if (!Enabled)
        {
            return (ExperimentArm.Control, ExposureReason.Disabled);
        }

        if (!Targeting.Matches(callerId, zip))
        {
            return (ExperimentArm.Control, ExposureReason.NotTargeted);
        }

        // Same caller, same arm on every order; only an anonymous request falls back to its own order ID.
        var bucket = ExperimentBucketer.Bucket(Id, Salt, callerId ?? orderId);
        var arm = bucket < TreatmentPercentage ? ExperimentArm.Treatment : ExperimentArm.Control;

        return (arm, ExposureReason.Bucketed);
    }
}
