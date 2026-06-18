using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using ProductEntity = GiseBsPayGateway.Entities.Product;
using CustomerEntity = GiseBsPayGateway.Entities.Customer;

namespace GiseBsPayGateway.Services;

public class StripeService : IStripeService
{
    private readonly ApplicationDbContext _db;
    private readonly IStripeSettingsProvider _stripeSettings;
    private readonly ILogger<StripeService> _logger;

    public StripeService(ApplicationDbContext db, IStripeSettingsProvider stripeSettings, ILogger<StripeService> logger)
    {
        _db = db;
        _stripeSettings = stripeSettings;
        _logger = logger;
    }

    private async Task ConfigureStripeAsync(CancellationToken cancellationToken)
    {
        var settings = await _stripeSettings.GetActiveAsync(cancellationToken);

        if (settings is null || string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            throw new InvalidOperationException(
                "Stripe n'est pas configuré. Créez /opt/apps/gisebs-pay-gateway/secrets.json sur le serveur ou configurez les clés dans l'admin.");
        }

        StripeConfiguration.ApiKey = settings.SecretKey;
    }

    public async Task<string> EnsureStripeProductAsync(ProductEntity product, CancellationToken cancellationToken = default)
    {
        await ConfigureStripeAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(product.StripeProductId))
        {
            if (await TryEnsureStripeProductTaxCodeAsync(product.StripeProductId, cancellationToken))
            {
                return product.StripeProductId;
            }

            _logger.LogWarning(
                "Produit Stripe {StripeProductId} introuvable pour {ProductCode}, recréation",
                product.StripeProductId,
                product.ProductCode);

            product.StripeProductId = null;
            product.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var service = new ProductService();
        var createOptions = new ProductCreateOptions
        {
            Name = product.Name,
            Description = product.Description,
            TaxCode = StripeTaxDefaults.DigitalProductTaxCode,
            Metadata = new Dictionary<string, string>
            {
                ["product_code"] = product.ProductCode,
                ["client_app_id"] = product.ClientApplicationId.ToString()
            }
        };

        Stripe.Product stripeProduct;
        try
        {
            stripeProduct = await service.CreateAsync(createOptions, cancellationToken: cancellationToken);
        }
        catch (StripeException ex) when (createOptions.TaxCode is not null)
        {
            _logger.LogWarning(ex, "Création produit Stripe sans tax code pour {ProductCode}", product.ProductCode);
            createOptions.TaxCode = null;
            stripeProduct = await service.CreateAsync(createOptions, cancellationToken: cancellationToken);
        }

        product.StripeProductId = stripeProduct.Id;
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return stripeProduct.Id;
    }

    public async Task<string> EnsureStripePriceAsync(PricingPlan plan, string stripeProductId, CancellationToken cancellationToken = default)
    {
        await ConfigureStripeAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(plan.StripePriceId))
        {
            if (await StripePriceExistsAsync(plan.StripePriceId, cancellationToken))
            {
                return plan.StripePriceId;
            }

            _logger.LogWarning(
                "Prix Stripe {StripePriceId} introuvable pour le plan {PlanCode}, recréation",
                plan.StripePriceId,
                plan.PlanCode);

            plan.StripePriceId = null;
            plan.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var service = new PriceService();
        var options = new PriceCreateOptions
        {
            Product = stripeProductId,
            UnitAmount = (long)(plan.Amount * 100),
            Currency = plan.Currency.ToLowerInvariant(),
            TaxBehavior = StripeTaxDefaults.PriceTaxBehaviorExclusive,
            Metadata = new Dictionary<string, string>
            {
                ["plan_code"] = plan.PlanCode
            }
        };

        if (plan.BillingInterval != BillingInterval.OneTime)
        {
            options.Recurring = new PriceRecurringOptions
            {
                Interval = plan.BillingInterval == BillingInterval.Yearly ? "year" : "month"
            };
        }

        var stripePrice = await service.CreateAsync(options, cancellationToken: cancellationToken);
        plan.StripePriceId = stripePrice.Id;
        plan.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return stripePrice.Id;
    }

    public async Task<string?> GetOrCreateStripeCustomerAsync(CustomerEntity customer, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(customer.StripeCustomerId))
        {
            return customer.StripeCustomerId;
        }

        await ConfigureStripeAsync(cancellationToken);
        var service = new CustomerService();
        var stripeCustomer = await service.CreateAsync(new CustomerCreateOptions
        {
            Email = customer.Email,
            Name = customer.FullName,
            Metadata = new Dictionary<string, string>
            {
                ["customer_code"] = customer.CustomerCode,
                ["client_app_id"] = customer.ClientApplicationId.ToString()
            }
        }, cancellationToken: cancellationToken);

        customer.StripeCustomerId = stripeCustomer.Id;
        customer.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return stripeCustomer.Id;
    }

    public async Task<(string SessionId, string? Url, string? ClientSecret)> CreateCheckoutSessionAsync(
        PaymentTransaction payment,
        CustomerEntity customer,
        PricingPlan plan,
        string successUrl,
        string cancelUrl,
        int? trialDays = null,
        bool embedded = false,
        BillingAddressDto? billingAddress = null,
        CustomerUpdateDto? customerUpdate = null,
        CancellationToken cancellationToken = default)
    {
        await ConfigureStripeAsync(cancellationToken);

        var stripeProductId = await EnsureStripeProductAsync(payment.Product, cancellationToken);
        var stripePriceId = await EnsureStripePriceAsync(plan, stripeProductId, cancellationToken);
        var stripeCustomerId = await GetOrCreateStripeCustomerAsync(customer, cancellationToken)
            ?? throw new InvalidOperationException("Impossible de créer ou récupérer le client Stripe.");

        var hasPrefilledBillingAddress = billingAddress is not null && !string.IsNullOrWhiteSpace(billingAddress.Line1);
        if (hasPrefilledBillingAddress)
        {
            var formattedAddress = StripeAddressFormatter.Format(billingAddress!);
            await ApplyStripeCustomerAddressAsync(stripeCustomerId, formattedAddress, cancellationToken);
        }

        var (billingAddressCollection, customerUpdateAddress) =
            StripeCheckoutTaxOptions.Resolve(hasPrefilledBillingAddress, customerUpdate);

        var sessionService = new SessionService();
        var options = new SessionCreateOptions
        {
            Mode = plan.BillingInterval == BillingInterval.OneTime ? "payment" : "subscription",
            Customer = stripeCustomerId,
            // Stripe Tax : activer dans le Dashboard (Settings → Tax), ajouter les enregistrements fiscaux canadiens (GST/HST/QST).
            AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true },
            BillingAddressCollection = billingAddressCollection,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = stripePriceId,
                    Quantity = 1
                }
            ],
            Metadata = new Dictionary<string, string>
            {
                ["payment_code"] = payment.PaymentCode,
                ["customer_code"] = customer.CustomerCode,
                ["product_code"] = payment.Product.ProductCode,
                ["plan_code"] = plan.PlanCode
            }
        };

        if (!string.IsNullOrWhiteSpace(customerUpdateAddress))
        {
            options.CustomerUpdate = new SessionCustomerUpdateOptions
            {
                Address = customerUpdateAddress
            };
        }

        if (embedded)
        {
            options.UiMode = "embedded_page";
            options.ReturnUrl = successUrl.Contains("{CHECKOUT_SESSION_ID}", StringComparison.Ordinal)
                ? successUrl
                : AppendQuery(successUrl, "session_id={CHECKOUT_SESSION_ID}");
        }
        else
        {
            options.SuccessUrl = successUrl;
            options.CancelUrl = cancelUrl;
        }

        if (plan.BillingInterval != BillingInterval.OneTime)
        {
            options.SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["payment_code"] = payment.PaymentCode
                }
            };

            if (trialDays is > 0)
                options.SubscriptionData.TrialPeriodDays = trialDays.Value;
        }

        var session = await sessionService.CreateAsync(options, cancellationToken: cancellationToken);
        _logger.LogInformation("Session Stripe créée {SessionId} pour paiement {PaymentCode} (embedded={Embedded})", session.Id, payment.PaymentCode, embedded);
        return (session.Id, session.Url, session.ClientSecret);
    }

    private static string AppendQuery(string url, string query)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}{query}";
    }

    private static async Task ApplyStripeCustomerAddressAsync(
        string stripeCustomerId,
        BillingAddressDto billingAddress,
        CancellationToken cancellationToken)
    {
        var service = new CustomerService();
        await service.UpdateAsync(stripeCustomerId, new CustomerUpdateOptions
        {
            Address = new AddressOptions
            {
                Line1 = billingAddress.Line1,
                Line2 = billingAddress.Line2,
                City = billingAddress.City,
                State = billingAddress.State,
                PostalCode = billingAddress.PostalCode,
                Country = billingAddress.Country
            }
        }, cancellationToken: cancellationToken);
    }

    private static async Task<bool> TryEnsureStripeProductTaxCodeAsync(
        string stripeProductId,
        CancellationToken cancellationToken)
    {
        var service = new ProductService();
        try
        {
            var product = await service.GetAsync(stripeProductId, cancellationToken: cancellationToken);
            if (product.TaxCode is not null)
            {
                return true;
            }

            await service.UpdateAsync(stripeProductId, new ProductUpdateOptions
            {
                TaxCode = StripeTaxDefaults.DigitalProductTaxCode
            }, cancellationToken: cancellationToken);

            return true;
        }
        catch (StripeException ex) when (StripeErrorMessages.IsResourceMissing(ex))
        {
            return false;
        }
    }

    private static async Task<bool> StripePriceExistsAsync(string stripePriceId, CancellationToken cancellationToken)
    {
        var service = new PriceService();
        try
        {
            await service.GetAsync(stripePriceId, cancellationToken: cancellationToken);
            return true;
        }
        catch (StripeException ex) when (StripeErrorMessages.IsResourceMissing(ex))
        {
            return false;
        }
    }

    public async Task CancelSubscriptionAsync(string stripeSubscriptionId, bool cancelImmediately, CancellationToken cancellationToken = default)
    {
        await ConfigureStripeAsync(cancellationToken);
        var service = new SubscriptionService();

        if (cancelImmediately)
        {
            await service.CancelAsync(stripeSubscriptionId, cancellationToken: cancellationToken);
        }
        else
        {
            await service.UpdateAsync(stripeSubscriptionId, new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = true
            }, cancellationToken: cancellationToken);
        }
    }
}
