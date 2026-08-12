using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Services.MobileMoney;

/// <summary>Stub MTN MoMo Collections direct — désactivé jusqu'aux credentials officiels Cameroun.</summary>
public sealed class MtnMomoDirectGateway : IMobileMoneyGateway
{
    public const string Code = "mtn_direct";

    public string ProviderCode => Code;

    public Task<PaymentInitiationResult> InitiateAsync(
        MobileMoneyPaymentRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PaymentInitiationResult(
            false, null, PaymentStatus.Failed, null, null, null,
            "NOT_SUPPORTED",
            "MTN MoMo direct non activé. Utilisez CamPay (phase 1).",
            NotSupported: true));

    public Task<PaymentStatusResult> GetStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PaymentStatusResult(
            false, PaymentStatus.RequiresReview, null, null, null, null,
            "NOT_SUPPORTED", "MTN MoMo direct non activé.", NotSupported: true));

    public Task<RefundResult> RefundAsync(
        MobileMoneyRefundRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new RefundResult(false, true, null, null, "MTN MoMo direct NotSupported."));

    public Task<WebhookValidationResult> ValidateWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebhookValidationResult(false, "MTN MoMo direct non activé."));

    public Task<MobileMoneyWebhookEventModel> ParseWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("MTN MoMo direct non activé.");

    public Task<ProviderHealthResult> GetHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProviderHealthResult(false, "MTN MoMo direct désactivé (credentials manquants)."));
}
