using Stripe;
using Stripe.Checkout;
using Stripe.Tax;

namespace GiseBsPayGateway.Services;

public record StripeBalanceTransactionDetails(
    decimal Fee,
    decimal Net,
    decimal GrossAmount,
    string BalanceTransactionId);

public interface IStripePaymentDetailsService
{
    Task<StripeBalanceTransactionDetails?> GetBalanceTransactionDetailsAsync(
        string? paymentIntentId,
        CancellationToken cancellationToken = default);

    Task<Session?> GetCheckoutSessionAsync(
        string? sessionId,
        CancellationToken cancellationToken = default);

    Task<string?> GetStripeTaxTransactionIdAsync(
        string? paymentIntentId,
        CancellationToken cancellationToken = default);
}

public class StripePaymentDetailsService : IStripePaymentDetailsService
{
    private readonly IStripeSettingsProvider _stripeSettings;
    private readonly ILogger<StripePaymentDetailsService> _logger;

    public StripePaymentDetailsService(
        IStripeSettingsProvider stripeSettings,
        ILogger<StripePaymentDetailsService> logger)
    {
        _stripeSettings = stripeSettings;
        _logger = logger;
    }

    public async Task<StripeBalanceTransactionDetails?> GetBalanceTransactionDetailsAsync(
        string? paymentIntentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            return null;
        }

        try
        {
            var settings = await _stripeSettings.GetActiveAsync(cancellationToken);
            if (settings is null || string.IsNullOrWhiteSpace(settings.SecretKey))
            {
                return null;
            }

            StripeConfiguration.ApiKey = settings.SecretKey;

            var intentService = new PaymentIntentService();
            var intent = await intentService.GetAsync(
                paymentIntentId,
                new PaymentIntentGetOptions
                {
                    Expand = ["latest_charge.balance_transaction"]
                },
                cancellationToken: cancellationToken);

            var charge = intent.LatestCharge;
            if (charge is null)
            {
                return null;
            }

            var balanceTransaction = charge.BalanceTransaction;
            if (balanceTransaction is null && !string.IsNullOrWhiteSpace(charge.BalanceTransactionId))
            {
                balanceTransaction = await new BalanceTransactionService().GetAsync(
                    charge.BalanceTransactionId,
                    cancellationToken: cancellationToken);
            }

            if (balanceTransaction is null)
            {
                return null;
            }

            return new StripeBalanceTransactionDetails(
                balanceTransaction.Fee / 100m,
                balanceTransaction.Net / 100m,
                balanceTransaction.Amount / 100m,
                balanceTransaction.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de récupérer la balance transaction pour {PaymentIntentId}", paymentIntentId);
            return null;
        }
    }

    public async Task<Session?> GetCheckoutSessionAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        try
        {
            var settings = await _stripeSettings.GetActiveAsync(cancellationToken);
            if (settings is null || string.IsNullOrWhiteSpace(settings.SecretKey))
            {
                return null;
            }

            StripeConfiguration.ApiKey = settings.SecretKey;

            return await new SessionService().GetAsync(
                sessionId,
                new SessionGetOptions
                {
                    Expand = ["total_details.breakdown.taxes.rate"]
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de récupérer la session Checkout {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<string?> GetStripeTaxTransactionIdAsync(
        string? paymentIntentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            return null;
        }

        try
        {
            var settings = await _stripeSettings.GetActiveAsync(cancellationToken);
            if (settings is null || string.IsNullOrWhiteSpace(settings.SecretKey))
            {
                return null;
            }

            StripeConfiguration.ApiKey = settings.SecretKey;

            var association = await new AssociationService().FindAsync(
                new AssociationFindOptions { PaymentIntent = paymentIntentId },
                cancellationToken: cancellationToken);

            var committed = association?.TaxTransactionAttempts?
                .FirstOrDefault(x => string.Equals(x.Status, "committed", StringComparison.OrdinalIgnoreCase));

            return committed?.Committed?.Transaction;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de récupérer la tax transaction pour {PaymentIntentId}", paymentIntentId);
            return null;
        }
    }
}
