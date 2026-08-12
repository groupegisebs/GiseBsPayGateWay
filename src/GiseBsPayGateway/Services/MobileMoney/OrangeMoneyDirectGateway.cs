using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Services.MobileMoney;

/// <summary>Stub Orange Money direct — désactivé jusqu'aux credentials officiels Cameroun.</summary>
public sealed class OrangeMoneyDirectGateway : IMobileMoneyGateway
{
    public const string Code = "orange_direct";

    public string ProviderCode => Code;

    public Task<PaymentInitiationResult> InitiateAsync(
        MobileMoneyPaymentRequest request,
        CancellationToken cancellationToken = default) =>
        NotSupportedInit();

    public Task<PaymentStatusResult> GetStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PaymentStatusResult(
            false, PaymentStatus.RequiresReview, null, null, null, null,
            "NOT_SUPPORTED", "Orange Money direct non activé.", NotSupported: true));

    public Task<RefundResult> RefundAsync(
        MobileMoneyRefundRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new RefundResult(false, true, null, null, "Orange Money direct NotSupported."));

    public Task<WebhookValidationResult> ValidateWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebhookValidationResult(false, "Orange Money direct non activé."));

    public Task<MobileMoneyWebhookEventModel> ParseWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Orange Money direct non activé.");

    public Task<ProviderHealthResult> GetHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProviderHealthResult(false, "Orange Money direct désactivé (credentials manquants)."));

    private static Task<PaymentInitiationResult> NotSupportedInit() =>
        Task.FromResult(new PaymentInitiationResult(
            false, null, PaymentStatus.Failed, null, null, null,
            "NOT_SUPPORTED",
            "Orange Money direct non activé. Utilisez CamPay (phase 1).",
            NotSupported: true));
}
