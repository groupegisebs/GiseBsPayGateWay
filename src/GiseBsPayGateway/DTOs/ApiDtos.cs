namespace GiseBsPayGateway.DTOs;

public record BillingAddressDto(
    string Line1,
    string? Line2,
    string City,
    string? State,
    string PostalCode,
    string Country);

public record CustomerUpdateDto(string Address);

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
    bool Embedded = false,
    BillingAddressDto? BillingAddress = null,
    CustomerUpdateDto? CustomerUpdate = null);

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
    string? FailureReason,
    string? StripeCheckoutSessionId,
    string? StripePaymentIntentId,
    string? InvoiceNumber,
    string? InvoiceDownloadUrl,
    decimal? AmountSubtotal = null,
    decimal? TaxAmount = null,
    decimal? GrossAmount = null,
    decimal? StripeFee = null,
    decimal? NetAmount = null,
    string? StripeBalanceTransactionId = null,
    string? BillingCountry = null,
    string? BillingState = null);

public record InvoiceResponse(
    string InvoiceNumber,
    string Status,
    decimal Amount,
    string Currency,
    DateTime InvoiceDate,
    DateTime? PaidAt,
    string? PaymentCode,
    string? StripePaymentIntentId,
    string? StripeCheckoutSessionId,
    string? StripeInvoiceId,
    string DownloadUrl,
    decimal? AmountSubtotal = null,
    decimal? TaxAmount = null,
    decimal? GrossAmount = null,
    decimal? StripeFee = null,
    decimal? NetAmount = null,
    string? StripeBalanceTransactionId = null,
    string? BillingCountry = null,
    string? BillingState = null);

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

public record CreateProductRequest(
    string ProductCode,
    string Name,
    string? Description,
    bool SyncToStripe = false);

public record CreatePricingPlanRequest(
    string PlanCode,
    string Name,
    decimal Amount,
    string Currency,
    string? BillingInterval = null,
    bool SyncToStripe = false);

public record CreateCatalogItemRequest(
    string ProductCode,
    string ProductName,
    string? Description,
    string PlanCode,
    string PlanName,
    decimal Amount,
    string Currency,
    string? BillingInterval = null,
    bool SyncToStripe = true);

public record ProductResponse(
    string ProductCode,
    string Name,
    string? Description,
    bool IsActive,
    string? StripeProductId,
    DateTime CreatedAt,
    IReadOnlyList<PricingPlanResponse>? Plans = null);

public record PricingPlanResponse(
    string PlanCode,
    string Name,
    decimal Amount,
    string Currency,
    string BillingInterval,
    bool IsActive,
    string? StripePriceId,
    DateTime CreatedAt);

public record CatalogItemResponse(ProductResponse Product, PricingPlanResponse Plan);

public record TaxCalculationRequest(
    BillingAddressDto BillingAddress,
    string? Currency = null,
    long? AmountMinorUnits = null);

public record TaxComponentDto(string Code, string Name, decimal Rate, string Type);

public record TaxCalculationResponse(
    string JurisdictionCode,
    decimal EstimatedTaxRate,
    IReadOnlyList<string> TaxLabels,
    IReadOnlyList<TaxComponentDto> Components,
    string Source = "stripe");
