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
    CustomerUpdateDto? CustomerUpdate = null,
    /// <summary>Ex. ["card"], ["paypal"]. Null = méthodes Dashboard Stripe.</summary>
    IReadOnlyList<string>? PaymentMethodTypes = null);

public record CheckoutSessionResponse(
    string PaymentCode,
    string CheckoutUrl,
    string SessionId,
    string Status,
    string? ClientSecret = null,
    string? PublishableKey = null,
    string StripeMode = "PROD");

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
    string? BillingState = null,
    BillingAddressDto? BillingAddress = null,
    IReadOnlyList<CollectedTaxLineDto>? TaxBreakdown = null);

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

public record CollectedTaxLineDto(string Code, string Name, decimal Rate, decimal Amount, string Type);

public record CollectedTaxSummaryDto(
    string PaymentCode,
    string TransactionReference,
    DateTime CollectedAt,
    string? BillingCountry,
    string? BillingState,
    string? BillingCity,
    string? BillingPostalCode,
    decimal AmountSubtotal,
    decimal TaxAmountTotal,
    decimal GrossAmount,
    string Currency,
    string? StripeTaxTransactionId,
    IReadOnlyList<CollectedTaxLineDto> TaxBreakdown,
    BillingAddressDto? BillingAddress);

public record TaxCalculationResponse(
    string JurisdictionCode,
    decimal EstimatedTaxRate,
    IReadOnlyList<string> TaxLabels,
    IReadOnlyList<TaxComponentDto> Components,
    string Source = "stripe");

// --- Stripe Connect / Payouts ---

public record CreateConnectAccountRequest(
    string ExternalReference,
    string? CountryCode = "CA",
    string? DefaultCurrency = "cad",
    string? Email = null,
    string? AccountType = "express",
    string? BusinessType = null,
    string? BusinessUrl = null,
    string? ProductDescription = null);

public record ConnectAccountResponse(
    string ExternalAccountId,
    string ExternalReference,
    string Country,
    string Currency,
    string? MaskedEmail,
    string Status,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    bool DetailsSubmitted,
    string? RequirementsCurrentlyDueJson);

public record CreateConnectAccountLinkRequest(
    string ExternalAccountId,
    string ReturnUrl,
    string RefreshUrl);

public record ConnectAccountLinkResponse(
    string ExternalAccountId,
    string Url,
    DateTime ExpiresAt);

public record CreateConnectTransferRequest(
    string DestinationAccountId,
    long AmountMinor,
    string Currency,
    string IdempotencyKey,
    string? Description = null,
    Dictionary<string, string>? Metadata = null);

public record ConnectTransferResponse(
    string TransferId,
    string IdempotencyKey,
    string DestinationAccountId,
    long AmountMinor,
    string Currency,
    string Status,
    string? FailureCode = null,
    string? FailureMessage = null);

// ── Payout Mobile Money ──────────────────────────────────────────────────────

/// <summary>Stub Mobile Money payout — Phase 4.</summary>
public record MobileMoneyValidateRequest(
    string CountryCode,
    string OperatorCode,
    string PhoneNumber,
    string AccountHolderName);

public record MobileMoneyValidateResponse(
    bool IsValid,
    string? MaskedPhone,
    string? ExternalToken,
    string? Message);

public record RegisterMobileMoneyRecipientRequest(
    string ExternalReference,
    string CountryCode,
    string OperatorCode,
    string PhoneNumber,
    string AccountHolderName);

public record CreateDisbursementRequestDto(
    string ExternalReference,
    string IdempotencyKey,
    string SellerExternalId,
    string? SellerDisplayName,
    string ProviderCode,
    string DestinationMasked,
    string? DestinationToken,
    long AmountMinor,
    string Currency,
    string CountryCode,
    Dictionary<string, string>? Metadata = null);

public record DisbursementRequestResponse(
    Guid Id,
    string ExternalReference,
    string IdempotencyKey,
    string ProviderCode,
    string DestinationMasked,
    long AmountMinor,
    string Currency,
    string CountryCode,
    string Status,
    bool ReconciliationChecked,
    string? ProviderPayoutId,
    string? FailureMessage);

public record PayPalOAuthStartRequest(string ExternalReference, string? ReturnUrl = null);
public record PayPalOAuthStartResponse(string AuthorizationUrl, string State);
public record PayPalLinkedAccountResponse(string ExternalReference, string? MaskedEmail, string Status, string? PayerId);

// ── Collecte Mobile Money (MTN MoMo Collections + Orange WebPay CM) ───────────

public record MobileMoneyChargeRequest(
    string CustomerCode,
    string Email,
    string? FullName,
    string? ExternalUserId,
    string ProductCode,
    string PlanCode,
    /// <summary>ORANGE | MTN</summary>
    string Channel,
    /// <summary>Numéro camerounais (requis pour MTN). Optionnel pour Orange WebPay.</summary>
    string? PhoneNumber = null,
    /// <summary>Code ISO pays du payeur (ex. CM). Défaut : CM.</summary>
    string? BillingCountryCode = null,
    string? MetadataJson = null,
    string? Description = null,
    /// <summary>Retour navigateur après Orange WebPay.</summary>
    string? ReturnUrl = null,
    string? CancelUrl = null);

public record MobileMoneyChargeResponse(
    string PaymentCode,
    string Status,
    /// <summary>Montant TTC encaissé (toujours taxé).</summary>
    decimal Amount,
    string Currency,
    string Channel,
    string PhoneMasked,
    string? ProviderReference,
    DateTime? ExpiresAtUtc,
    string? Instruction,
    string? UssdHint,
    decimal AmountExclusive = 0,
    decimal TaxAmount = 0,
    decimal TaxRatePercent = 0,
    string? TaxName = null,
    string? BillingCountryCode = null,
    /// <summary>URL de paiement Orange WebPay (redirection client).</summary>
    string? PaymentUrl = null);

public record MobileMoneyStatusResponse(
    string PaymentCode,
    string Status,
    string? RawProviderStatus,
    decimal Amount,
    string Currency,
    string? Channel,
    string? PhoneMasked,
    string? ProviderReference,
    DateTime? PaidAt,
    DateTime? ExpiresAtUtc,
    string? FailureCode,
    string? FailureReason,
    decimal? AmountExclusive = null,
    decimal? TaxAmount = null,
    decimal? TaxRatePercent = null,
    string? TaxName = null,
    string? BillingCountryCode = null);

public record AfricanTaxQuoteRequest(
    decimal AmountExclusive,
    string Currency,
    string CountryCode);

public record AfricanTaxQuoteResponse(
    string CountryCode,
    string CountryName,
    string TaxName,
    decimal TaxRatePercent,
    decimal AmountExclusive,
    decimal TaxAmount,
    decimal AmountInclusive,
    string Currency);