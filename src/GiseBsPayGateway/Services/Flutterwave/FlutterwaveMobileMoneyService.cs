using System.Text.Json;
using GiseBsPayGateway.Constants;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Enums;
using GiseBsPayGateway.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ICurrencyConversionService = GiseBsPayGateway.Services.ICurrencyConversionService;

namespace GiseBsPayGateway.Services.Flutterwave;

public interface IFlutterwaveMobileMoneyService
{
    IReadOnlyList<MobileMoneyNetworkDto> ListNetworks(string? countryCode = null);

    IReadOnlyList<MobileMoneyCountryDto> ListCountries();

    Task<MobileMoneyQuoteResponse> QuoteAsync(
        decimal amount,
        string fromCurrency,
        string countryCode,
        CancellationToken ct = default);

    Task<MobileMoneyChargeResponse> ChargeAsync(
        ClientApplication app,
        CreateMobileMoneyChargeRequest request,
        CancellationToken ct = default);

    Task HandleWebhookAsync(string? verifHash, string rawBody, CancellationToken ct = default);

    Task RefreshPendingAsync(PaymentTransaction payment, CancellationToken ct = default);
}

public sealed class FlutterwaveMobileMoneyService(
    ApplicationDbContext db,
    IFlutterwaveApiClient flutterwave,
    ICurrencyConversionService conversion,
    IOptions<FlutterwaveOptions> options,
    IAuditService audit,
    ILogger<FlutterwaveMobileMoneyService> logger) : IFlutterwaveMobileMoneyService
{
    public IReadOnlyList<MobileMoneyNetworkDto> ListNetworks(string? countryCode = null) =>
        MobileMoneyNetworkCatalog.ForCountry(countryCode)
            .Select(x => new MobileMoneyNetworkDto(
                x.CountryCode,
                x.CountryName,
                x.Currency,
                x.Network,
                x.NetworkLabel,
                x.PhoneCountryCode))
            .ToList();

    public IReadOnlyList<MobileMoneyCountryDto> ListCountries() => MobileMoneyNetworkCatalog.ListCountries();

    public async Task<MobileMoneyQuoteResponse> QuoteAsync(
        decimal amount,
        string fromCurrency,
        string countryCode,
        CancellationToken ct = default)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Montant invalide.");

        var country = MobileMoneyNetworkCatalog.ForCountry(countryCode).FirstOrDefault()
            ?? throw new InvalidOperationException($"Pays Mobile Money inconnu : '{countryCode}'.");

        var converted = await conversion.ConvertAsync(amount, fromCurrency, country.Currency, ct);
        if (converted < 1m && CatalogOptions.IsZeroDecimalCurrency(country.Currency))
            throw new InvalidOperationException(
                $"Montant converti trop bas ({converted} {country.Currency}).");

        return new MobileMoneyQuoteResponse(
            amount,
            fromCurrency.Trim().ToUpperInvariant(),
            converted,
            country.Currency.ToUpperInvariant(),
            country.CountryCode,
            country.CountryName);
    }

    public async Task<MobileMoneyChargeResponse> ChargeAsync(
        ClientApplication app,
        CreateMobileMoneyChargeRequest request,
        CancellationToken ct = default)
    {
        if (!flutterwave.IsConfigured)
            throw new InvalidOperationException(
                "Flutterwave v4 non configuré (Flutterwave:ClientId / Flutterwave:ClientSecret).");

        var network = MobileMoneyNetworkCatalog.Find(request.CountryCode, request.Network)
            ?? throw new InvalidOperationException(
                $"Opérateur '{request.Network}' non supporté pour le pays '{request.CountryCode}'. " +
                "Devises Flutterwave : XOF (BF/CI/SN), XAF (CM), GHS, KES, RWF, TZS, UGX, ZMW. " +
                "Voir GET /api/mobile-money/networks.");

        var product = await db.Products
            .Include(x => x.PricingPlans)
            .FirstOrDefaultAsync(x =>
                x.ClientApplicationId == app.Id &&
                x.ProductCode == request.ProductCode &&
                x.IsActive, ct)
            ?? throw new InvalidOperationException($"Produit '{request.ProductCode}' introuvable.");

        var plan = product.PricingPlans.FirstOrDefault(x =>
                      x.PlanCode == request.PlanCode &&
                      x.IsActive &&
                      x.Currency.Equals(network.Currency, StringComparison.OrdinalIgnoreCase))
                  ?? product.PricingPlans.FirstOrDefault(x => x.PlanCode == request.PlanCode && x.IsActive)
                  ?? throw new InvalidOperationException($"Plan '{request.PlanCode}' introuvable.");

        var customer = await db.Customers
            .FirstOrDefaultAsync(x => x.ClientApplicationId == app.Id && x.CustomerCode == request.CustomerCode, ct);

        if (customer is null)
        {
            customer = new Customer
            {
                ClientApplicationId = app.Id,
                CustomerCode = request.CustomerCode,
                Email = request.Email,
                FullName = request.FullName,
                ExternalUserId = request.ExternalUserId,
                Phone = request.PhoneNumber
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            customer.Email = request.Email;
            customer.FullName = request.FullName ?? customer.FullName;
            customer.ExternalUserId = request.ExternalUserId ?? customer.ExternalUserId;
            customer.Phone = request.PhoneNumber;
            customer.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        var sourceCurrency = string.IsNullOrWhiteSpace(request.SourceCurrency)
            ? plan.Currency
            : request.SourceCurrency;
        var sourceAmount = request.Amount ?? plan.Amount;
        if (sourceAmount <= 0)
            throw new InvalidOperationException("Montant invalide.");

        var currency = network.Currency;
        var amount = await conversion.ConvertAsync(sourceAmount, sourceCurrency, currency, ct);
        if (amount <= 0)
            throw new InvalidOperationException("Montant converti invalide.");

        var paymentCode = $"PAY-{app.AppCode.ToUpperInvariant()}-{Guid.NewGuid():N}"[..32];
        var reference = Guid.NewGuid().ToString("D");
        var (_, national) = MobileMoneyNetworkCatalog.SplitPhone(request.PhoneNumber, network.PhoneCountryCode);
        var displayPhone = $"{network.PhoneCountryCode}{national}";

        var payment = new PaymentTransaction
        {
            ClientApplicationId = app.Id,
            CustomerId = customer.Id,
            ProductId = product.Id,
            PricingPlanId = plan.Id,
            PaymentCode = paymentCode,
            Status = PaymentStatus.Pending,
            Amount = amount,
            Currency = currency.ToLowerInvariant(),
            OriginalAmount = sourceAmount,
            OriginalCurrency = sourceCurrency.Trim().ToLowerInvariant(),
            Provider = "flutterwave",
            FlutterwaveTxRef = reference,
            MobileMoneyNetwork = network.Network,
            MobileMoneyPhone = displayPhone,
            MobileMoneyCountry = network.CountryCode,
            BillingCountry = network.CountryCode,
            Product = product,
            PricingPlan = plan,
            Customer = customer
        };

        db.PaymentTransactions.Add(payment);
        await db.SaveChangesAsync(ct);

        try
        {
            var charge = await flutterwave.ChargeMobileMoneyAsync(new FlutterwaveMobileMoneyChargeRequest(
                Reference: reference,
                Amount: amount,
                Currency: currency,
                Email: request.Email,
                PhoneNumber: request.PhoneNumber,
                PhoneCountryCode: network.PhoneCountryCode,
                Network: network.Network,
                FullName: request.FullName ?? customer.FullName,
                ScenarioKey: request.ScenarioKey), ct);

            payment.FlutterwaveTransactionId = charge.FlutterwaveChargeId;
            ApplyStatus(payment, charge.FlutterwaveStatus, charge.FlutterwaveChargeId);
            payment.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await audit.LogAsync(
                "FlutterwaveMobileMoneyCharge",
                nameof(PaymentTransaction),
                payment.Id.ToString(),
                charge.Success,
                $"PaymentCode={paymentCode};Network={network.Network};Country={network.CountryCode};ChargeId={charge.FlutterwaveChargeId};Status={charge.FlutterwaveStatus}",
                app.AppCode);

            return new MobileMoneyChargeResponse(
                PaymentCode: paymentCode,
                Status: payment.Status.ToString(),
                Provider: "flutterwave",
                TxRef: reference,
                FlutterwaveTransactionId: payment.FlutterwaveTransactionId,
                Amount: amount,
                Currency: currency,
                CountryCode: network.CountryCode,
                Network: network.Network,
                PhoneNumber: displayPhone,
                Instruction: charge.Instruction,
                RedirectUrl: charge.RedirectUrl,
                Message: charge.Message,
                OriginalAmount: sourceAmount,
                OriginalCurrency: sourceCurrency.Trim().ToUpperInvariant());
        }
        catch (Exception ex)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason = ex.Message;
            payment.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogError(ex, "Flutterwave v4 mobile money charge failed {PaymentCode}", paymentCode);
            throw;
        }
    }

    public async Task HandleWebhookAsync(string? verifHash, string rawBody, CancellationToken ct = default)
    {
        var expectedHash = options.Value.WebhookSecret;
        if (!string.IsNullOrWhiteSpace(expectedHash))
        {
            if (string.IsNullOrWhiteSpace(verifHash) ||
                !string.Equals(verifHash.Trim(), expectedHash.Trim(), StringComparison.Ordinal))
            {
                logger.LogWarning("Flutterwave webhook: signature/verif-hash invalide");
                throw new UnauthorizedAccessException("Flutterwave webhook signature invalide.");
            }
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
        var root = doc.RootElement;
        var eventType = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        var data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : root;

        // v4: data.reference + data.status (succeeded) + data.id (chg_…)
        var reference = data.TryGetProperty("reference", out var r) ? r.GetString()
            : data.TryGetProperty("tx_ref", out var tr) ? tr.GetString() : null;
        var status = data.TryGetProperty("status", out var st) ? st.GetString() : null;
        var chargeId = data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(reference))
        {
            logger.LogWarning("Flutterwave webhook sans reference (type={Type})", eventType);
            return;
        }

        var payment = await db.PaymentTransactions
            .FirstOrDefaultAsync(x => x.FlutterwaveTxRef == reference, ct);

        if (payment is null)
        {
            logger.LogWarning("Flutterwave webhook: paiement inconnu reference={Reference}", reference);
            return;
        }

        if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.Cancelled)
            return;

        ApplyStatus(payment, status, chargeId);
        payment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(
            "FlutterwaveWebhook",
            nameof(PaymentTransaction),
            payment.Id.ToString(),
            true,
            $"Type={eventType};Reference={reference};Status={status};ChargeId={chargeId}");
    }

    public async Task RefreshPendingAsync(PaymentTransaction payment, CancellationToken ct = default)
    {
        if (payment.Provider != "flutterwave" || payment.Status != PaymentStatus.Pending)
            return;

        if (string.IsNullOrWhiteSpace(payment.FlutterwaveTransactionId))
            return;

        var verified = await flutterwave.GetChargeAsync(payment.FlutterwaveTransactionId, ct);
        if (verified is null)
            return;

        ApplyStatus(payment, verified.Status, verified.ChargeId);
        payment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static void ApplyStatus(PaymentTransaction payment, string? status, string? chargeId)
    {
        if (!string.IsNullOrWhiteSpace(chargeId))
            payment.FlutterwaveTransactionId = chargeId;

        if (string.IsNullOrWhiteSpace(status))
            return;

        // v4: succeeded | pending | failed — v3 legacy: successful
        if (status.Equals("succeeded", StringComparison.OrdinalIgnoreCase)
            || status.Equals("successful", StringComparison.OrdinalIgnoreCase)
            || status.Equals("success", StringComparison.OrdinalIgnoreCase)
            || status.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            payment.Status = PaymentStatus.Succeeded;
            payment.PaidAt ??= DateTime.UtcNow;
            payment.GrossAmount ??= payment.Amount;
            payment.AmountSubtotal ??= payment.Amount;
            payment.FailureReason = null;
        }
        else if (status.Equals("failed", StringComparison.OrdinalIgnoreCase)
                 || status.Equals("failure", StringComparison.OrdinalIgnoreCase)
                 || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase))
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason ??= $"Flutterwave: {status}";
        }
    }
}
