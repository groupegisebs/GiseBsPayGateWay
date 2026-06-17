namespace GiseBsPayGateway.DTOs;

public record CreateCheckoutSessionRequest(
    string CustomerCode,
    string Email,
    string? FullName,
    string? ExternalUserId,
    string ProductCode,
    string PlanCode,
    string SuccessUrl,
    string CancelUrl,
    string? MetadataJson,
    int? TrialDays,
    bool Embedded = false);

public record CheckoutSessionResponse(
    string PaymentCode,
    string CheckoutUrl,
    string SessionId,
    string Status,
    string? ClientSecret = null,
    string? PublishableKey = null);

public record PaymentResponse(
    string PaymentCode,
    string Status,
    decimal Amount,
    string Currency,
    string CustomerCode,
    string ProductCode,
    string PlanCode,
    DateTime CreatedAt,
    DateTime? PaidAt,
    string? FailureReason);

public record SubscriptionResponse(
    string SubscriptionCode,
    string Status,
    string CustomerCode,
    string ProductCode,
    string PlanCode,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

public record CancelSubscriptionRequest(string SubscriptionCode, bool CancelImmediately);

public record CancelSubscriptionResponse(string SubscriptionCode, string Status, DateTime? CancelledAt);

public record ApiErrorResponse(string Error, string? Details);

public record JwtTokenRequest(string AppCode, string ApiKey);

public record JwtTokenResponse(string AccessToken, DateTime ExpiresAt, string TokenType);
