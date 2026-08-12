using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GiseBsPayGateway.Enums;

namespace GiseBsPayGateway.Services.MobileMoney;

/// <summary>Simulateur déterministe pour Environment=Local (aucun appel réseau).</summary>
public sealed class LocalSimulatedMobileMoneyGateway : IMobileMoneyGateway
{
    public const string Code = "campay";

    private readonly ConcurrentDictionary<string, SimTxn> _store = new();

    public string ProviderCode => Code;

    public Task<PaymentInitiationResult> InitiateAsync(
        MobileMoneyPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        // Scénarios déterministes via suffixe du numéro :
        // ...0000 = succès immédiat au prochain GetStatus
        // ...0001 = échec solde insuffisant
        // ...0002 = expiration
        // sinon = pending jusqu'à webhook simulé / GetStatus "force"
        var providerRef = Guid.NewGuid().ToString("D");
        var scenario = ResolveScenario(request.PhoneNumber);
        var status = scenario switch
        {
            SimScenario.InsufficientFunds => PaymentStatus.Failed,
            SimScenario.Expire => PaymentStatus.PendingCustomerConfirmation,
            _ => PaymentStatus.PendingCustomerConfirmation
        };

        _store[providerRef] = new SimTxn(
            request.InternalReference,
            request.Amount,
            request.Currency,
            request.Channel,
            status,
            scenario,
            DateTime.UtcNow);

        if (scenario == SimScenario.InsufficientFunds)
        {
            return Task.FromResult(new PaymentInitiationResult(
                true, providerRef, PaymentStatus.Failed, "FAILED",
                null, null, "INSUFFICIENT_FUNDS", "Solde insuffisant (simulateur)."));
        }

        var ussd = request.Channel == "ORANGE" ? "#150*50#" : "*126#";
        var paymentUrl = request.Channel.Equals("ORANGE", StringComparison.OrdinalIgnoreCase)
            ? $"https://local.simulator/orange-webpay?order={Uri.EscapeDataString(request.InternalReference)}&ref={providerRef}"
            : null;
        return Task.FromResult(new PaymentInitiationResult(
            true, providerRef, PaymentStatus.PendingCustomerConfirmation, "PENDING",
            request.Channel.Equals("ORANGE", StringComparison.OrdinalIgnoreCase)
                ? "Redirection simulée vers Orange Money WebPay."
                : "Consultez votre téléphone et confirmez la demande de paiement. Ne communiquez jamais votre code secret Mobile Money.",
            ussd, null, null, PaymentUrl: paymentUrl));
    }

    public Task<PaymentStatusResult> GetStatusAsync(
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        if (!_store.TryGetValue(providerReference, out var txn))
        {
            return Task.FromResult(new PaymentStatusResult(
                false, PaymentStatus.RequiresReview, null, null, null, null,
                "UNKNOWN", "Transaction simulée inconnue."));
        }

        var status = txn.Scenario switch
        {
            SimScenario.Success => PaymentStatus.Succeeded,
            SimScenario.InsufficientFunds => PaymentStatus.Failed,
            SimScenario.Expire when DateTime.UtcNow - txn.CreatedAtUtc > TimeSpan.FromSeconds(2)
                => PaymentStatus.Expired,
            _ => txn.Status
        };

        _store[providerReference] = txn with { Status = status };
        var raw = status switch
        {
            PaymentStatus.Succeeded => "SUCCESSFUL",
            PaymentStatus.Failed => "FAILED",
            PaymentStatus.Expired => "EXPIRED",
            _ => "PENDING"
        };

        return Task.FromResult(new PaymentStatusResult(
            true, status, raw, txn.Amount, txn.Currency, txn.Channel, null, null));
    }

    public Task<RefundResult> RefundAsync(
        MobileMoneyRefundRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new RefundResult(false, true, null, null, "NotSupported (simulateur)."));

    public Task<WebhookValidationResult> ValidateWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebhookValidationResult(true, null));

    public async Task<MobileMoneyWebhookEventModel> ParseWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        request.Body.Position = 0;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
        var root = doc.RootElement;
        var reference = root.TryGetProperty("reference", out var r) ? r.GetString() : null;
        var external = root.TryGetProperty("external_reference", out var e) ? e.GetString() : null;
        var statusRaw = root.TryGetProperty("status", out var s) ? s.GetString() : "SUCCESSFUL";
        decimal? amount = null;
        if (root.TryGetProperty("amount", out var a))
        {
            if (a.ValueKind == JsonValueKind.Number && a.TryGetDecimal(out var d))
                amount = d;
            else if (a.ValueKind == JsonValueKind.String && decimal.TryParse(a.GetString(), out var ds))
                amount = ds;
        }

        var currency = root.TryGetProperty("currency", out var c) ? c.GetString() : "XAF";
        var op = root.TryGetProperty("operator", out var o) ? o.GetString() : null;
        var normalized = MobileMoneyPhoneValidator.MapCamPayStatus(statusRaw);

        if (!string.IsNullOrWhiteSpace(reference) && _store.TryGetValue(reference, out var txn))
            _store[reference] = txn with { Status = normalized };

        return new MobileMoneyWebhookEventModel(
            reference ?? external,
            "transaction.status",
            reference,
            external,
            normalized,
            statusRaw,
            amount,
            currency,
            op,
            hash,
            payload);
    }

    public Task<ProviderHealthResult> GetHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProviderHealthResult(true, "Simulateur local OK."));

    /// <summary>Force le succès d'une transaction pending (tests / webhook local).</summary>
    public bool TryForceSucceed(string providerReference)
    {
        if (!_store.TryGetValue(providerReference, out var txn))
            return false;
        _store[providerReference] = txn with { Status = PaymentStatus.Succeeded, Scenario = SimScenario.Success };
        return true;
    }

    private static SimScenario ResolveScenario(string phone)
    {
        if (phone.EndsWith("0001", StringComparison.Ordinal))
            return SimScenario.InsufficientFunds;
        if (phone.EndsWith("0002", StringComparison.Ordinal))
            return SimScenario.Expire;
        return SimScenario.Success;
    }

    private enum SimScenario { Success, InsufficientFunds, Expire }

    private sealed record SimTxn(
        string InternalReference,
        decimal Amount,
        string Currency,
        string Channel,
        PaymentStatus Status,
        SimScenario Scenario,
        DateTime CreatedAtUtc);
}
