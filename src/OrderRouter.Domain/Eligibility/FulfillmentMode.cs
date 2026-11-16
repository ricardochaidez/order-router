namespace OrderRouter.Domain.Eligibility;

/// <summary>How an eligible supplier would ship the order (FR-09).</summary>
public enum FulfillmentMode
{
    /// <summary>The supplier serves the customer's ZIP and delivers locally.</summary>
    Local,

    /// <summary>The supplier ships by mail: it is mail-only, or it does not serve the customer's ZIP.</summary>
    Mail,
}
