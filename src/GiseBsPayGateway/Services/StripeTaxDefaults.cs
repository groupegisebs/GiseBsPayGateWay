namespace GiseBsPayGateway.Services;

/// <summary>
/// Stripe Tax defaults for digital / SaaS catalog items (Stripe Tax product tax codes).
/// </summary>
public static class StripeTaxDefaults
{
    /// <summary>Software as a service (SaaS) — electronically supplied services.</summary>
    public const string DigitalProductTaxCode = "txcd_10103001";

    public const string PriceTaxBehaviorExclusive = "exclusive";
}
