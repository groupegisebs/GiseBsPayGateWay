namespace GiseBsPayGateway.Services;

public interface IApiKeyService
{
    (string RawKey, string Prefix, string Hash) GenerateApiKey();
    string HashApiKey(string rawKey);
    bool VerifyApiKey(string rawKey, string hash);
}

public interface IAuditService
{
    Task LogAsync(string action, string entityType, string? entityId, bool isSuccess, string? details = null, string? appCode = null, string? userId = null, string? userName = null, string? ipAddress = null);
}

public interface IStripeService
{
    Task<string> EnsureStripeProductAsync(Entities.Product product, CancellationToken cancellationToken = default);
    Task<string> EnsureStripePriceAsync(Entities.PricingPlan plan, string stripeProductId, CancellationToken cancellationToken = default);
    Task<(string SessionId, string? Url, string? ClientSecret)> CreateCheckoutSessionAsync(Entities.PaymentTransaction payment, Entities.Customer customer, Entities.PricingPlan plan, string successUrl, string cancelUrl, int? trialDays = null, bool embedded = false, DTOs.BillingAddressDto? billingAddress = null, DTOs.CustomerUpdateDto? customerUpdate = null, CancellationToken cancellationToken = default);
    Task CancelSubscriptionAsync(string stripeSubscriptionId, bool cancelImmediately, CancellationToken cancellationToken = default);
    Task SetCancelAtPeriodEndAsync(string stripeSubscriptionId, bool cancelAtPeriodEnd, CancellationToken cancellationToken = default);
    Task<string?> GetOrCreateStripeCustomerAsync(Entities.Customer customer, CancellationToken cancellationToken = default);
    /// <summary>Devise verrouillée du customer Stripe (null si pas encore fixée).</summary>
    Task<string?> GetCustomerLockedCurrencyAsync(string stripeCustomerId, CancellationToken cancellationToken = default);
}

public interface IPaymentService
{
    Task<DTOs.CheckoutSessionResponse> CreateCheckoutSessionAsync(Entities.ClientApplication app, DTOs.CreateCheckoutSessionRequest request, CancellationToken cancellationToken = default);
    Task<DTOs.PaymentResponse?> GetPaymentByCodeAsync(Entities.ClientApplication app, string paymentCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DTOs.SubscriptionResponse>> GetCustomerSubscriptionsAsync(Entities.ClientApplication app, string customerCode, CancellationToken cancellationToken = default);
    Task<DTOs.CancelSubscriptionResponse> CancelSubscriptionAsync(Entities.ClientApplication app, DTOs.CancelSubscriptionRequest request, CancellationToken cancellationToken = default);
}

public interface IWebhookService
{
    Task ProcessStripeWebhookAsync(string json, string signatureHeader, CancellationToken cancellationToken = default);
    Task<bool> TryCompleteFromCheckoutSessionAsync(Entities.PaymentTransaction payment, Stripe.Checkout.Session session, CancellationToken cancellationToken = default);
}

public interface IJwtTokenService
{
    DTOs.JwtTokenResponse GenerateToken(Entities.ClientApplication app);
}

public interface ICatalogService
{
    Task<DTOs.ProductResponse> CreateProductAsync(Entities.ClientApplication app, DTOs.CreateProductRequest request, CancellationToken cancellationToken = default);
    Task<DTOs.PricingPlanResponse> CreatePlanAsync(Entities.ClientApplication app, string productCode, DTOs.CreatePricingPlanRequest request, CancellationToken cancellationToken = default);
    Task<DTOs.CatalogItemResponse> CreateCatalogItemAsync(Entities.ClientApplication app, DTOs.CreateCatalogItemRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DTOs.ProductResponse>> ListProductsAsync(Entities.ClientApplication app, CancellationToken cancellationToken = default);
    Task<DTOs.ProductResponse?> GetProductAsync(Entities.ClientApplication app, string productCode, CancellationToken cancellationToken = default);
    Task<DTOs.ProductResponse> SyncProductToStripeAsync(Entities.ClientApplication app, string productCode, CancellationToken cancellationToken = default);
}

public interface IDashboardService
{
    Task<DashboardStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

public record DashboardStats(
    decimal TotalRevenue,
    int SuccessfulPayments,
    int FailedPayments,
    int ActiveSubscriptions,
    int ClientApplications,
    int PendingPayments);
