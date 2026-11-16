namespace OrderRouter.Domain.Routing.Experiments;

/// <summary>
/// Why a request got the arm it got for one experiment (ADR-0005, AB-04). Serialized as <c>disabled</c>,
/// <c>not_targeted</c> and <c>bucketed</c> by the API's snake_case enum policy.
/// </summary>
public enum ExposureReason
{
    /// <summary>The experiment is switched off, so the request is on control.</summary>
    Disabled,

    /// <summary>The experiment is on but the caller or ZIP is outside its targeting, so the request is on control.</summary>
    NotTargeted,

    /// <summary>The request was targeted and the salted bucket decided the arm.</summary>
    Bucketed,
}
