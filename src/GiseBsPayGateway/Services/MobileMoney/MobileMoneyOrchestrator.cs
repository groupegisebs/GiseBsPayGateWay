using System.Security.Cryptography;
using System.Text;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Options;
using GiseBsPayGateway.Services.Tax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GiseBsPayGateway.Services.MobileMoney;

public interface IMobileMoneyOrchestrator
{
    Task<MobileMoneyChargeResponse> ChargeAsync(
        ClientApplication app,
        MobileMoneyChargeRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<MobileMoneyStatusResponse?> RefreshStatusAsync(
        ClientApplication app,
        string paymentCode,
        CancellationToken cancellationToken = default);

    Task<(int StatusCode, object Body)> HandleWebhookAsync(
        string provider,
        HttpRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MobileMoneyOrchestrator : IMobileMoneyOrchestrator
{
    private readonly ApplicationDbContext _db;
    private readonly IEnumerable<IMobileMoneyGateway> _gateways;
    private readonly LocalSimulatedMobileMoneyGateway _localSim;
    private readonly MobileMoneyOptions _options;
    private readonly IAfricanTaxService _africanTax;
    private readonly IAuditService _auditService;
    private readonly ILogger<MobileMoneyOrchestrator> _logger;

    public MobileMoneyOrchestrator(
        ApplicationDbContext db,
        IEnumerable<IMobileMoneyGateway> gateways,
        LocalSimulatedMobileMoneyGateway localSim,
        IOptions<MobileMoneyOptions> options,
        IAfricanTaxService africanTax,
        IAuditService auditService,
        ILogger<MobileMoneyOrchestrator> logger)
    {
        _db = db;
        _gateways = gateways;
        _localSim = localSim;
        _options = options.Value;
        _africanTax = africanTax;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<MobileMoneyChargeResponse> ChargeAsync(
        ClientApplication app,
        MobileMoneyChargeRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(_options.Currency, "XAF", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Devise Mobile Money configurée invalide (attendu XAF).");

        var channel = MobileMoneyPhoneValidator.NormalizeChannel(request.Channel);
        if (string.IsNullOrEmpty(channel))
            throw new InvalidOperationException("Canal invalide. Utilisez ORANGE ou MTN.");

        if (!MobileMoneyPhoneValidator.TryNormalizeCameroonPhone(request.PhoneNumber, out var phone, out var masked))
            throw new InvalidOperationException("Numéro camerounais invalide. Format attendu : +2376XXXXXXXX.");

        var billingCountry = string.IsNullOrWhiteSpace(request.BillingCountryCode)
            ? (_options.Country ?? "CM")
            : request.BillingCountryCode.Trim().ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _db.PaymentTransactions
                .Include(x => x.PricingPlan)
                .FirstOrDefaultAsync(
                    x => x.ClientApplicationId == app.Id && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existing is not null)
                return MapCharge(existing);
        }

        var product = await _db.Products
            .Include(x => x.PricingPlans)
            .FirstOrDefaultAsync(
                x => x.ClientApplicationId == app.Id && x.ProductCode == request.ProductCode && x.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException($"Produit '{request.ProductCode}' introuvable.");

        if (!product.PricingPlans.Any(x => x.PlanCode == request.PlanCode && x.IsActive))
            throw new InvalidOperationException($"Plan '{request.PlanCode}' introuvable.");

        // Pas de conversion FX pour Mobile Money (CDC §2.2) — plan XAF obligatoire.
        var plan = product.PricingPlans.FirstOrDefault(x =>
                x.PlanCode.Equals(request.PlanCode, StringComparison.OrdinalIgnoreCase) &&
                x.IsActive &&
                x.Currency.Equals("xaf", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "Le plan de paiement Mobile Money doit exister en devise XAF (pas de conversion automatique).");

        // Montant catalogue = HT ; montant encaissé = toujours TTC (taxe pays du payeur).
        var tax = _africanTax.Calculate(plan.Amount, "XAF", billingCountry);

        var customer = await _db.Customers
            .FirstOrDefaultAsync(
                x => x.ClientApplicationId == app.Id && x.CustomerCode == request.CustomerCode,
                cancellationToken);

        if (customer is null)
        {
            customer = new Customer
            {
                ClientApplicationId = app.Id,
                CustomerCode = request.CustomerCode,
                Email = request.Email,
                FullName = request.FullName,
                ExternalUserId = request.ExternalUserId,
                Phone = masked
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            customer.Email = request.Email;
            customer.FullName = request.FullName ?? customer.FullName;
            customer.ExternalUserId = request.ExternalUserId ?? customer.ExternalUserId;
            customer.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var gateway = ResolveActiveGateway();
        var paymentCode = $"PAY-{app.AppCode.ToUpperInvariant()}-{Guid.NewGuid():N}"[..32];
        var expires = DateTime.UtcNow.AddMinutes(Math.Max(5, _options.ChargeExpiryMinutes));

        var payment = new PaymentTransaction
        {
            ClientApplicationId = app.Id,
            CustomerId = customer.Id,
            ProductId = product.Id,
            PricingPlanId = plan.Id,
            PaymentCode = paymentCode,
            Status = PaymentStatus.Pending,
            Amount = tax.AmountInclusive,
            AmountSubtotal = tax.AmountExclusive,
            TaxAmount = tax.TaxAmount,
            GrossAmount = tax.AmountInclusive,
            Currency = plan.Currency,
            Provider = gateway.ProviderCode,
            MobileMoneyChannel = channel,
            PhoneMasked = masked,
            BillingCountry = tax.CountryCode,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            ExpiresAtUtc = expires,
            Product = product,
            PricingPlan = plan,
            Customer = customer
        };

        _db.PaymentTransactions.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        var init = await gateway.InitiateAsync(new MobileMoneyPaymentRequest(
            paymentCode,
            channel,
            phone,
            tax.AmountInclusive,
            "XAF",
            request.Description ?? $"Paiement {paymentCode}",
            payment.IdempotencyKey), cancellationToken);

        if (!init.Success && init.NotSupported)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = init.FailureCode;
            payment.FailureReason = init.FailureMessage;
            payment.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(init.FailureMessage ?? "Fournisseur Mobile Money non disponible.");
        }

        payment.ProviderReference = init.ProviderReference;
        payment.RawProviderStatus = init.RawProviderStatus;
        payment.FailureCode = init.FailureCode;
        payment.FailureReason = init.FailureMessage;

        if (!MobileMoneyStateMachine.TryTransition(payment.Status, init.NormalizedStatus, out var next))
        {
            payment.Status = PaymentStatus.RequiresReview;
        }
        else
        {
            payment.Status = next;
        }

        if (payment.Status == PaymentStatus.Succeeded)
            payment.PaidAt = DateTime.UtcNow;

        payment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "MobileMoneyChargeCreated",
            nameof(PaymentTransaction),
            payment.Id.ToString(),
            true,
            $"PaymentCode={paymentCode};Channel={channel};Provider={gateway.ProviderCode};Status={payment.Status};Country={tax.CountryCode};HT={tax.AmountExclusive};Tax={tax.TaxAmount};TTC={tax.AmountInclusive}",
            app.AppCode);

        return MapCharge(payment, init.Instruction, init.UssdHint);
    }

    public async Task<MobileMoneyStatusResponse?> RefreshStatusAsync(
        ClientApplication app,
        string paymentCode,
        CancellationToken cancellationToken = default)
    {
        var payment = await _db.PaymentTransactions
            .FirstOrDefaultAsync(
                x => x.ClientApplicationId == app.Id && x.PaymentCode == paymentCode,
                cancellationToken);

        if (payment is null)
            return null;

        if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
            return MapStatus(payment);

        if (payment.ExpiresAtUtc is { } exp && exp < DateTime.UtcNow &&
            payment.Status is PaymentStatus.Pending or PaymentStatus.PendingCustomerConfirmation)
        {
            if (MobileMoneyStateMachine.TryTransition(payment.Status, PaymentStatus.Expired, out var expired))
            {
                payment.Status = expired;
                payment.RawProviderStatus = "EXPIRED";
                payment.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return MapStatus(payment);
        }

        if (string.IsNullOrWhiteSpace(payment.ProviderReference))
            return MapStatus(payment);

        var gateway = ResolveGatewayByCode(payment.Provider);
        var status = await gateway.GetStatusAsync(payment.ProviderReference, cancellationToken);
        if (!status.Success)
            return MapStatus(payment);

        ApplyProviderStatus(payment, status.NormalizedStatus, status.RawProviderStatus, status.Amount, status.Currency);
        payment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return MapStatus(payment);
    }

    public async Task<(int StatusCode, object Body)> HandleWebhookAsync(
        string provider,
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        if (provider is "orange" or "mtn")
            return (StatusCodes.Status501NotImplemented, new { error = $"Webhook {provider} non activé (phase 2)." });

        if (!string.Equals(provider, "campay", StringComparison.OrdinalIgnoreCase))
            return (StatusCodes.Status404NotFound, new { error = "Fournisseur inconnu." });

        var gateway = ResolveActiveGateway();
        var validation = await gateway.ValidateWebhookAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            _logger.LogWarning("Webhook CamPay rejeté : {Error}", validation.ErrorMessage);
            return (StatusCodes.Status401Unauthorized, new { error = "Signature invalide." });
        }

        MobileMoneyWebhookEventModel evt;
        try
        {
            evt = await gateway.ParseWebhookAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Parse webhook CamPay échoué");
            return (StatusCodes.Status401Unauthorized, new { error = ex.Message });
        }

        var duplicate = await _db.MobileMoneyWebhookEvents
            .AnyAsync(x => x.PayloadHash == evt.PayloadHash, cancellationToken);
        if (duplicate)
            return (StatusCodes.Status200OK, new { received = true, duplicate = true });

        if (!string.IsNullOrWhiteSpace(evt.ProviderEventId))
        {
            var dupEvent = await _db.MobileMoneyWebhookEvents.AnyAsync(
                x => x.Provider == CamPayMobileMoneyGateway.Code && x.ProviderEventId == evt.ProviderEventId,
                cancellationToken);
            if (dupEvent)
                return (StatusCodes.Status200OK, new { received = true, duplicate = true });
        }

        var webhookRow = new MobileMoneyWebhookEvent
        {
            Provider = CamPayMobileMoneyGateway.Code,
            ProviderEventId = evt.ProviderEventId,
            EventType = evt.EventType,
            PayloadHash = evt.PayloadHash,
            Payload = RedactPayload(evt.RawPayload),
            ProcessingStatus = WebhookProcessingStatus.Received
        };
        _db.MobileMoneyWebhookEvents.Add(webhookRow);
        await _db.SaveChangesAsync(cancellationToken);

        var payment = await FindPaymentAsync(evt, cancellationToken);
        if (payment is null)
        {
            webhookRow.ProcessingStatus = WebhookProcessingStatus.Failed;
            webhookRow.ErrorMessage = "Transaction inconnue";
            webhookRow.ProcessedAt = DateTime.UtcNow;
            webhookRow.ProcessingResult = "UnknownTransaction";
            await _db.SaveChangesAsync(cancellationToken);
            await _auditService.LogAsync("MobileMoneyWebhookUnknown", "Webhook", webhookRow.Id.ToString(), false,
                "Transaction inconnue", null);
            return (StatusCodes.Status200OK, new { received = true, review = "unknown_transaction" });
        }

        ApplyProviderStatus(payment, evt.NormalizedStatus, evt.RawProviderStatus, evt.Amount, evt.Currency);
        payment.UpdatedAt = DateTime.UtcNow;

        webhookRow.ProcessingStatus = WebhookProcessingStatus.Processed;
        webhookRow.ProcessedAt = DateTime.UtcNow;
        webhookRow.ProcessingResult = payment.Status.ToString();
        await _db.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "MobileMoneyWebhookProcessed",
            nameof(PaymentTransaction),
            payment.Id.ToString(),
            true,
            $"Status={payment.Status};ProviderRef={payment.ProviderReference}",
            payment.ClientApplication?.AppCode);

        return (StatusCodes.Status200OK, new { received = true, paymentCode = payment.PaymentCode, status = payment.Status.ToString() });
    }

    private void ApplyProviderStatus(
        PaymentTransaction payment,
        PaymentStatus incoming,
        string? raw,
        decimal? amount,
        string? currency)
    {
        payment.RawProviderStatus = raw;

        if (amount.HasValue && Math.Abs(amount.Value - payment.Amount) > 0.5m)
        {
            payment.Status = PaymentStatus.RequiresReview;
            payment.FailureCode = "AMOUNT_MISMATCH";
            payment.FailureReason = "Montant webhook divergent.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(currency) &&
            !string.Equals(currency, payment.Currency, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(currency, "XAF", StringComparison.OrdinalIgnoreCase))
        {
            payment.Status = PaymentStatus.RequiresReview;
            payment.FailureCode = "CURRENCY_MISMATCH";
            payment.FailureReason = "Devise webhook divergente.";
            return;
        }

        if (!MobileMoneyStateMachine.TryTransition(payment.Status, incoming, out var next))
        {
            if (payment.Status == PaymentStatus.Succeeded)
                return;

            payment.Status = PaymentStatus.RequiresReview;
            payment.FailureCode = "INVALID_TRANSITION";
            payment.FailureReason = $"Transition {payment.Status} → {incoming} refusée.";
            return;
        }

        payment.Status = next;
        if (next == PaymentStatus.Succeeded)
            payment.PaidAt ??= DateTime.UtcNow;
        if (next == PaymentStatus.Failed)
            payment.FailureCode ??= "FAILED";
    }

    private async Task<PaymentTransaction?> FindPaymentAsync(
        MobileMoneyWebhookEventModel evt,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(evt.ExternalReference))
        {
            var byCode = await _db.PaymentTransactions
                .Include(x => x.ClientApplication)
                .FirstOrDefaultAsync(x => x.PaymentCode == evt.ExternalReference, ct);
            if (byCode is not null)
                return byCode;
        }

        if (!string.IsNullOrWhiteSpace(evt.ProviderReference))
        {
            return await _db.PaymentTransactions
                .Include(x => x.ClientApplication)
                .FirstOrDefaultAsync(x => x.ProviderReference == evt.ProviderReference, ct);
        }

        return null;
    }

    private IMobileMoneyGateway ResolveActiveGateway()
    {
        var campay = _options.Providers.CamPay;
        if (!campay.Enabled)
            throw new InvalidOperationException("CamPay est désactivé.");

        if (campay.Environment.Equals("Local", StringComparison.OrdinalIgnoreCase))
            return _localSim;

        return _gateways.First(g => g is CamPayMobileMoneyGateway);
    }

    private IMobileMoneyGateway ResolveGatewayByCode(string providerCode)
    {
        if (providerCode.Equals(CamPayMobileMoneyGateway.Code, StringComparison.OrdinalIgnoreCase) &&
            _options.Providers.CamPay.Environment.Equals("Local", StringComparison.OrdinalIgnoreCase))
            return _localSim;

        return _gateways.FirstOrDefault(g => g.ProviderCode.Equals(providerCode, StringComparison.OrdinalIgnoreCase))
            ?? _localSim;
    }

    private MobileMoneyChargeResponse MapCharge(
        PaymentTransaction payment,
        string? instruction = null,
        string? ussd = null)
    {
        var exclusive = payment.AmountSubtotal ?? payment.Amount;
        var taxAmount = payment.TaxAmount ?? 0m;
        var (rate, taxName) = ResolveCatalogTax(payment.BillingCountry);

        return new(
            payment.PaymentCode,
            payment.Status.ToString(),
            payment.Amount,
            payment.Currency.ToUpperInvariant(),
            payment.MobileMoneyChannel ?? "",
            payment.PhoneMasked ?? "",
            payment.ProviderReference,
            payment.ExpiresAtUtc,
            instruction ?? (payment.Status == PaymentStatus.PendingCustomerConfirmation
                ? "Consultez votre téléphone et confirmez la demande de paiement. Ne communiquez jamais votre code secret Mobile Money."
                : null),
            ussd,
            exclusive,
            taxAmount,
            rate,
            taxName,
            payment.BillingCountry);
    }

    private MobileMoneyStatusResponse MapStatus(PaymentTransaction payment)
    {
        var exclusive = payment.AmountSubtotal;
        var taxAmount = payment.TaxAmount;
        var (rate, taxName) = ResolveCatalogTax(payment.BillingCountry);

        return new(
            payment.PaymentCode,
            payment.Status.ToString(),
            payment.RawProviderStatus,
            payment.Amount,
            payment.Currency.ToUpperInvariant(),
            payment.MobileMoneyChannel,
            payment.PhoneMasked,
            payment.ProviderReference,
            payment.PaidAt,
            payment.ExpiresAtUtc,
            payment.FailureCode,
            payment.FailureReason,
            exclusive,
            taxAmount,
            string.IsNullOrWhiteSpace(payment.BillingCountry) ? null : rate,
            taxName,
            payment.BillingCountry);
    }

    /// <summary>Taux catalogue admin / seed (pas le taux effectif après arrondi monétaire).</summary>
    private (decimal RatePercent, string? TaxName) ResolveCatalogTax(string? billingCountry)
    {
        if (_africanTax.TryGetRate(billingCountry, out var rateInfo))
            return (rateInfo.RatePercent, rateInfo.TaxName);
        return (0m, null);
    }

    private static string RedactPayload(string payload)
    {
        // Ne conserve qu'un extrait haché-compatible ; tronque les gros payloads.
        if (payload.Length <= 4000)
            return payload;
        return payload[..4000] + "…[truncated]";
    }
}
