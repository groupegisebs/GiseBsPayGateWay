using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Services.MobileMoney;

public interface IMobileMoneyGateway
{
    string ProviderCode { get; }

    Task<PaymentInitiationResult> InitiateAsync(
        MobileMoneyPaymentRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentStatusResult> GetStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default);

    Task<RefundResult> RefundAsync(
        MobileMoneyRefundRequest request,
        CancellationToken cancellationToken = default);

    Task<WebhookValidationResult> ValidateWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default);

    Task<MobileMoneyWebhookEventModel> ParseWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default);

    Task<ProviderHealthResult> GetHealthAsync(CancellationToken cancellationToken = default);
}

public sealed record MobileMoneyPaymentRequest(
    string InternalReference,
    string Channel,
    string PhoneNumber,
    decimal Amount,
    string Currency,
    string Description,
    string? IdempotencyKey,
    string? ReturnUrl = null,
    string? CancelUrl = null,
    string? NotifUrl = null);

public sealed record PaymentInitiationResult(
    bool Success,
    string? ProviderReference,
    PaymentStatus NormalizedStatus,
    string? RawProviderStatus,
    string? Instruction,
    string? UssdHint,
    string? FailureCode,
    string? FailureMessage,
    bool NotSupported = false,
    string? PaymentUrl = null);

public sealed record PaymentStatusResult(
    bool Success,
    PaymentStatus NormalizedStatus,
    string? RawProviderStatus,
    decimal? Amount,
    string? Currency,
    string? Operator,
    string? FailureCode,
    string? FailureMessage,
    bool NotSupported = false);

public sealed record MobileMoneyRefundRequest(
    string ProviderReference,
    decimal Amount,
    string Currency,
    string Reason);

public sealed record RefundResult(
    bool Success,
    bool NotSupported,
    string? ProviderReference,
    PaymentStatus? NormalizedStatus,
    string? FailureMessage);

public sealed record WebhookValidationResult(bool IsValid, string? ErrorMessage);

public sealed record MobileMoneyWebhookEventModel(
    string? ProviderEventId,
    string EventType,
    string? ProviderReference,
    string? ExternalReference,
    PaymentStatus NormalizedStatus,
    string? RawProviderStatus,
    decimal? Amount,
    string? Currency,
    string? Operator,
    string PayloadHash,
    string RawPayload);

public sealed record ProviderHealthResult(bool Healthy, string Message);
