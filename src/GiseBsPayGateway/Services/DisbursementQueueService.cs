using System.Text.Json;
using System.Text.RegularExpressions;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Services;

public interface IDisbursementQueueService
{
    Task<DisbursementRequestResponse> EnqueueAsync(ClientApplication app, CreateDisbursementRequestDto request, CancellationToken ct = default);
    Task<DisbursementRequestResponse?> GetAsync(ClientApplication app, string referenceOrId, CancellationToken ct = default);
    Task<IReadOnlyList<SellerDisbursementRequest>> ListPendingAsync(CancellationToken ct = default);
    Task<(bool Success, string? Error)> MarkReconciledAsync(Guid id, string adminUserId, string? notes, CancellationToken ct = default);
    Task<(bool Success, string? Error)> ApproveAndPayAsync(Guid id, string adminUserId, CancellationToken ct = default);
    Task<(bool Success, string? Error)> RejectAsync(Guid id, string adminUserId, string reason, CancellationToken ct = default);
    Task<(bool Success, string? Error)> HoldAsync(Guid id, string adminUserId, string reason, CancellationToken ct = default);
}

public interface IMobileMoneyPublicInfoService
{
    MobileMoneyValidateResponse ValidatePublicInfo(MobileMoneyValidateRequest request);
    Task<MobileMoneyRecipient> RegisterAsync(ClientApplication app, RegisterMobileMoneyRecipientRequest request, CancellationToken ct = default);
}

public class MobileMoneyPublicInfoService(ApplicationDbContext db) : IMobileMoneyPublicInfoService
{
    private static readonly Dictionary<string, Regex> PhonePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CM"] = new(@"^\+?237[62]\d{8}$"),
        ["SN"] = new(@"^\+?221[77]\d{7}$"),
        ["CI"] = new(@"^\+?225\d{8,10}$"),
        ["KE"] = new(@"^\+?254[17]\d{8}$"),
        ["GH"] = new(@"^\+?233\d{9}$"),
        ["NG"] = new(@"^\+?234\d{10}$"),
    };

    private static readonly HashSet<string> Operators = new(StringComparer.OrdinalIgnoreCase)
    {
        "mtn_momo", "orange_money", "wave", "mpesa", "taptapsend", "moov", "airtel"
    };

    public MobileMoneyValidateResponse ValidatePublicInfo(MobileMoneyValidateRequest request)
    {
        var country = (request.CountryCode ?? "").Trim().ToUpperInvariant();
        var op = (request.OperatorCode ?? "").Trim().ToLowerInvariant();
        var phone = NormalizePhone(request.PhoneNumber);
        var holder = (request.AccountHolderName ?? "").Trim();

        if (!Operators.Contains(op))
            return new MobileMoneyValidateResponse(false, null, null, "Opérateur non supporté.");
        if (holder.Length < 2)
            return new MobileMoneyValidateResponse(false, null, null, "Nom du titulaire requis (info publique).");
        if (phone.Length < 8)
            return new MobileMoneyValidateResponse(false, null, null, "Numéro de téléphone invalide.");
        if (PhonePatterns.TryGetValue(country, out var pattern) && !pattern.IsMatch(phone))
            return new MobileMoneyValidateResponse(false, null, null, $"Format téléphone invalide pour {country}.");

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        var masked = digits.Length >= 4
            ? $"+{digits[..Math.Min(3, digits.Length)]} ••• ••• {digits[^2..]}"
            : "••••";
        var token = $"mm_{op}_{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(phone + op)))[..16]}";
        return new MobileMoneyValidateResponse(true, masked, token, null);
    }

    public async Task<MobileMoneyRecipient> RegisterAsync(
        ClientApplication app,
        RegisterMobileMoneyRecipientRequest request,
        CancellationToken ct = default)
    {
        var validation = ValidatePublicInfo(new MobileMoneyValidateRequest(
            request.CountryCode, request.OperatorCode, request.PhoneNumber, request.AccountHolderName));
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Message ?? "Validation échouée.");

        var phone = NormalizePhone(request.PhoneNumber);
        var existing = await db.MobileMoneyRecipients.FirstOrDefaultAsync(x =>
            x.ClientApplicationId == app.Id && x.ExternalReference == request.ExternalReference, ct);

        if (existing is null)
        {
            existing = new MobileMoneyRecipient
            {
                ClientApplicationId = app.Id,
                ExternalReference = request.ExternalReference
            };
            db.MobileMoneyRecipients.Add(existing);
        }

        existing.CountryCode = request.CountryCode.Trim().ToUpperInvariant();
        existing.OperatorCode = request.OperatorCode.Trim().ToLowerInvariant();
        existing.AccountHolderName = request.AccountHolderName.Trim();
        existing.PhoneE164 = phone;
        existing.MaskedPhone = validation.MaskedPhone ?? "••••";
        existing.PublicAccountId = validation.ExternalToken;
        existing.Status = "registered";
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        var trimmed = phone.Trim().Replace(" ", "").Replace("-", "");
        if (!trimmed.StartsWith('+') && trimmed.All(char.IsDigit))
            return "+" + trimmed;
        return trimmed.StartsWith('+') ? trimmed : "+" + new string(trimmed.Where(char.IsDigit).ToArray());
    }
}

public class DisbursementQueueService(
    ApplicationDbContext db,
    ITransferService transferService,
    IPayPalPayoutService paypal,
    IPayoutCallbackNotifier callbackNotifier,
    ILogger<DisbursementQueueService> logger) : IDisbursementQueueService
{
    public async Task<DisbursementRequestResponse> EnqueueAsync(
        ClientApplication app,
        CreateDisbursementRequestDto request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new InvalidOperationException("IdempotencyKey obligatoire.");

        var existing = await db.SellerDisbursementRequests
            .FirstOrDefaultAsync(x => x.ClientApplicationId == app.Id && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
            return Map(existing);

        var channel = ParseChannel(request.ProviderCode);
        var entity = new SellerDisbursementRequest
        {
            ClientApplicationId = app.Id,
            ExternalReference = request.ExternalReference,
            IdempotencyKey = request.IdempotencyKey,
            SellerExternalId = request.SellerExternalId,
            SellerDisplayName = request.SellerDisplayName,
            Channel = channel,
            ProviderCode = request.ProviderCode,
            DestinationMasked = request.DestinationMasked,
            DestinationToken = request.DestinationToken,
            AmountMinor = request.AmountMinor,
            Currency = (request.Currency ?? "CAD").ToLowerInvariant(),
            CountryCode = (request.CountryCode ?? "CA").ToUpperInvariant(),
            Status = DisbursementStatus.PendingReview,
            MetadataJson = request.Metadata is null ? null : JsonSerializer.Serialize(request.Metadata)
        };
        db.SellerDisbursementRequests.Add(entity);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Disbursement enqueued {Id} {Channel} {Amount}", entity.Id, channel, entity.AmountMinor);
        return Map(entity);
    }

    public async Task<DisbursementRequestResponse?> GetAsync(ClientApplication app, string referenceOrId, CancellationToken ct = default)
    {
        SellerDisbursementRequest? entity = null;
        if (Guid.TryParse(referenceOrId, out var id))
            entity = await db.SellerDisbursementRequests.FirstOrDefaultAsync(x => x.ClientApplicationId == app.Id && x.Id == id, ct);
        entity ??= await db.SellerDisbursementRequests.FirstOrDefaultAsync(x =>
            x.ClientApplicationId == app.Id
            && (x.ExternalReference == referenceOrId || x.IdempotencyKey == referenceOrId), ct);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<SellerDisbursementRequest>> ListPendingAsync(CancellationToken ct = default)
        => await db.SellerDisbursementRequests
            .Include(x => x.ClientApplication)
            .Where(x => x.Status == DisbursementStatus.PendingReview
                        || x.Status == DisbursementStatus.NeedsReconciliation
                        || x.Status == DisbursementStatus.Approved
                        || x.Status == DisbursementStatus.Held)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<(bool Success, string? Error)> MarkReconciledAsync(Guid id, string adminUserId, string? notes, CancellationToken ct = default)
    {
        var entity = await db.SellerDisbursementRequests.FindAsync([id], ct);
        if (entity is null) return (false, "not_found");
        entity.ReconciliationChecked = true;
        entity.ReconciliationNotes = notes;
        entity.Status = DisbursementStatus.Approved;
        entity.ReviewedByUserId = adminUserId;
        entity.ReviewedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ApproveAndPayAsync(Guid id, string adminUserId, CancellationToken ct = default)
    {
        var entity = await db.SellerDisbursementRequests
            .Include(x => x.ClientApplication)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return (false, "not_found");
        if (!entity.ReconciliationChecked && entity.Status != DisbursementStatus.Approved)
            return (false, "rapprochement_requis");
        if (entity.Status is DisbursementStatus.Paid or DisbursementStatus.Submitted)
            return (false, "already_paid");

        entity.Status = DisbursementStatus.Queued;
        entity.ReviewedByUserId = adminUserId;
        entity.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            entity.Status = DisbursementStatus.Submitted;
            await db.SaveChangesAsync(ct);

            switch (entity.Channel)
            {
                case DisbursementChannel.StripeConnect:
                {
                    if (entity.ClientApplication is null || string.IsNullOrWhiteSpace(entity.DestinationToken))
                        return (false, "destination_manquante");
                    var transfer = await transferService.CreateTransferAsync(entity.ClientApplication, new CreateConnectTransferRequest(
                        entity.DestinationToken,
                        entity.AmountMinor,
                        entity.Currency,
                        entity.IdempotencyKey,
                        $"Disbursement {entity.ExternalReference}"), ct);
                    entity.ProviderPayoutId = transfer.TransferId;
                    entity.Status = DisbursementStatus.Paid;
                    break;
                }
                case DisbursementChannel.PayPal:
                {
                    var dest = entity.DestinationToken ?? entity.DestinationMasked;
                    if (entity.ClientApplication is not null && !string.IsNullOrWhiteSpace(entity.DestinationToken))
                    {
                        var linked = await paypal.GetLinkedAsync(entity.ClientApplication, entity.DestinationToken, ct);
                        if (linked?.PayerId is { Length: > 0 } payerId)
                            dest = payerId;
                    }

                    var (ok, batchId, error) = await paypal.SendPayoutAsync(
                        dest!, entity.AmountMinor, entity.Currency, entity.IdempotencyKey,
                        $"Disbursement {entity.ExternalReference}", ct);
                    if (!ok)
                    {
                        entity.Status = DisbursementStatus.Failed;
                        entity.FailureMessage = error;
                        await db.SaveChangesAsync(ct);
                        return (false, error);
                    }
                    entity.ProviderPayoutId = batchId;
                    entity.Status = DisbursementStatus.Paid;
                    break;
                }
                case DisbursementChannel.MobileMoney:
                {
                    // Infos publiques uniquement côté vendeur ; après rapprochement admin = exécution caisse / agrégateur.
                    if (entity.ClientApplication is not null && !string.IsNullOrWhiteSpace(entity.DestinationToken))
                    {
                        var recipient = await db.MobileMoneyRecipients.FirstOrDefaultAsync(x =>
                            x.ClientApplicationId == entity.ClientApplicationId
                            && (x.PublicAccountId == entity.DestinationToken || x.ExternalReference == entity.DestinationToken), ct);
                        if (recipient is not null)
                            entity.DestinationMasked = $"{recipient.OperatorCode} {recipient.MaskedPhone} ({recipient.AccountHolderName})";
                    }

                    entity.ProviderPayoutId = $"mm_ready_{entity.Id:N}"[..24];
                    entity.Status = DisbursementStatus.Paid;
                    entity.ReconciliationNotes = (entity.ReconciliationNotes ?? "")
                        + " | Mobile Money marqué payé après rapprochement admin (exécution opérateur/caisse).";
                    break;
                }
                default:
                    entity.Status = DisbursementStatus.Paid;
                    entity.ProviderPayoutId = $"manual_{entity.Id:N}"[..20];
                    break;
            }

            entity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            if (entity.ClientApplication is not null)
            {
                await callbackNotifier.NotifyAsync(entity.ClientApplication, "disbursement.paid", new
                {
                    id = entity.Id,
                    externalReference = entity.ExternalReference,
                    idempotencyKey = entity.IdempotencyKey,
                    status = entity.Status.ToString(),
                    providerPayoutId = entity.ProviderPayoutId,
                    amountMinor = entity.AmountMinor,
                    currency = entity.Currency
                }, ct);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ApproveAndPay failed for {Id}", id);
            entity.Status = DisbursementStatus.Failed;
            entity.FailureMessage = ex.Message;
            await db.SaveChangesAsync(ct);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? Error)> RejectAsync(Guid id, string adminUserId, string reason, CancellationToken ct = default)
    {
        var entity = await db.SellerDisbursementRequests.Include(x => x.ClientApplication).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return (false, "not_found");
        entity.Status = DisbursementStatus.Rejected;
        entity.RejectionReason = reason;
        entity.ReviewedByUserId = adminUserId;
        entity.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (entity.ClientApplication is not null)
        {
            await callbackNotifier.NotifyAsync(entity.ClientApplication, "disbursement.rejected", new
            {
                id = entity.Id,
                externalReference = entity.ExternalReference,
                idempotencyKey = entity.IdempotencyKey,
                reason
            }, ct);
        }
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> HoldAsync(Guid id, string adminUserId, string reason, CancellationToken ct = default)
    {
        var entity = await db.SellerDisbursementRequests.FindAsync([id], ct);
        if (entity is null) return (false, "not_found");
        entity.Status = DisbursementStatus.Held;
        entity.ReconciliationNotes = reason;
        entity.ReviewedByUserId = adminUserId;
        entity.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    private static DisbursementChannel ParseChannel(string providerCode) =>
        providerCode.ToLowerInvariant() switch
        {
            "stripe_connect" => DisbursementChannel.StripeConnect,
            "paypal" => DisbursementChannel.PayPal,
            "mtn_momo" or "orange_money" or "wave" or "mpesa" or "taptapsend" or "moov" or "airtel" or "mobile_money"
                => DisbursementChannel.MobileMoney,
            _ => DisbursementChannel.Manual
        };

    private static DisbursementRequestResponse Map(SellerDisbursementRequest e) => new(
        e.Id,
        e.ExternalReference,
        e.IdempotencyKey,
        e.ProviderCode,
        e.DestinationMasked,
        e.AmountMinor,
        e.Currency,
        e.CountryCode,
        e.Status.ToString(),
        e.ReconciliationChecked,
        e.ProviderPayoutId,
        e.FailureMessage);
}
