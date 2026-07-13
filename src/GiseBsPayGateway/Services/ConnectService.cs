using System.Text.Json;
using GiseBsPayGateway.Data;
using GiseBsPayGateway.DTOs;
using GiseBsPayGateway.Entities;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace GiseBsPayGateway.Services;

public interface IConnectService
{
    Task<ConnectAccountResponse> CreateAccountAsync(ClientApplication app, CreateConnectAccountRequest request, CancellationToken ct = default);
    Task<ConnectAccountLinkResponse> CreateAccountLinkAsync(ClientApplication app, CreateConnectAccountLinkRequest request, CancellationToken ct = default);
    Task<ConnectAccountResponse> GetAccountAsync(ClientApplication app, string externalAccountId, CancellationToken ct = default);
    Task SyncAccountFromStripeAsync(Account stripeAccount, CancellationToken ct = default);
}

public interface ITransferService
{
    Task<ConnectTransferResponse> CreateTransferAsync(ClientApplication app, CreateConnectTransferRequest request, CancellationToken ct = default);
    Task<ConnectTransferResponse?> GetTransferAsync(ClientApplication app, string transferId, CancellationToken ct = default);
    Task UpdateTransferStatusAsync(string stripeTransferId, string status, string? failureCode, string? failureMessage, CancellationToken ct = default);
}

public class ConnectService(
    ApplicationDbContext db,
    IStripeSettingsProvider stripeSettings,
    IAuditService auditService,
    ILogger<ConnectService> logger) : IConnectService
{
    private async Task ConfigureStripeAsync(CancellationToken cancellationToken)
    {
        var settings = await stripeSettings.GetActiveAsync(cancellationToken)
            ?? throw new InvalidOperationException("Stripe n'est pas configuré.");
        if (string.IsNullOrWhiteSpace(settings.SecretKey))
            throw new InvalidOperationException("Stripe n'est pas configuré.");
        StripeConfiguration.ApiKey = settings.SecretKey;
    }

    public async Task<ConnectAccountResponse> CreateAccountAsync(
        ClientApplication app,
        CreateConnectAccountRequest request,
        CancellationToken ct = default)
    {
        await ConfigureStripeAsync(ct);

        var existing = await db.ConnectedAccounts
            .FirstOrDefaultAsync(x =>
                x.ClientApplicationId == app.Id
                && x.ExternalReference == request.ExternalReference, ct);

        if (existing is not null)
            return Map(existing);

        var country = (request.CountryCode ?? "CA").Trim().ToUpperInvariant();
        var currency = (request.DefaultCurrency ?? "cad").Trim().ToLowerInvariant();
        var accountType = string.IsNullOrWhiteSpace(request.AccountType) ? "express" : request.AccountType.Trim().ToLowerInvariant();

        var options = new AccountCreateOptions
        {
            Type = accountType,
            Country = country,
            Email = request.Email,
            Capabilities = new AccountCapabilitiesOptions
            {
                Transfers = new AccountCapabilitiesTransfersOptions { Requested = true }
            },
            BusinessProfile = new AccountBusinessProfileOptions
            {
                Url = request.BusinessUrl,
                ProductDescription = request.ProductDescription ?? "Marketplace seller"
            },
            Metadata = new Dictionary<string, string>
            {
                ["app_code"] = app.AppCode,
                ["external_reference"] = request.ExternalReference
            }
        };

        if (!string.IsNullOrWhiteSpace(request.BusinessType))
            options.BusinessType = request.BusinessType;

        var service = new AccountService();
        Account account;
        try
        {
            account = await service.CreateAsync(options, cancellationToken: ct);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Échec création compte Connect pour {Ref}", request.ExternalReference);
            throw new InvalidOperationException(ex.Message);
        }

        var entity = new ConnectedAccount
        {
            ClientApplicationId = app.Id,
            ExternalReference = request.ExternalReference,
            StripeAccountId = account.Id,
            Country = country,
            Currency = currency,
            Email = request.Email,
            AccountType = accountType,
            ChargesEnabled = account.ChargesEnabled,
            PayoutsEnabled = account.PayoutsEnabled,
            DetailsSubmitted = account.DetailsSubmitted,
            Status = DeriveStatus(account),
            RequirementsCurrentlyDueJson = JsonSerializer.Serialize(account.Requirements?.CurrentlyDue ?? []),
            LastSyncedAt = DateTime.UtcNow
        };

        db.ConnectedAccounts.Add(entity);
        await db.SaveChangesAsync(ct);
        await auditService.LogAsync("ConnectAccountCreated", nameof(ConnectedAccount), entity.Id.ToString(), true, account.Id, app.AppCode);

        return Map(entity);
    }

    public async Task<ConnectAccountLinkResponse> CreateAccountLinkAsync(
        ClientApplication app,
        CreateConnectAccountLinkRequest request,
        CancellationToken ct = default)
    {
        await ConfigureStripeAsync(ct);

        var account = await ResolveAccountAsync(app, request.ExternalAccountId, ct);
        var service = new AccountLinkService();
        AccountLink link;
        try
        {
            link = await service.CreateAsync(new AccountLinkCreateOptions
            {
                Account = account.StripeAccountId,
                RefreshUrl = request.RefreshUrl,
                ReturnUrl = request.ReturnUrl,
                Type = "account_onboarding"
            }, cancellationToken: ct);
        }
        catch (StripeException ex)
        {
            throw new InvalidOperationException(ex.Message);
        }

        return new ConnectAccountLinkResponse(account.StripeAccountId, link.Url, link.ExpiresAt);
    }

    public async Task<ConnectAccountResponse> GetAccountAsync(
        ClientApplication app,
        string externalAccountId,
        CancellationToken ct = default)
    {
        await ConfigureStripeAsync(ct);
        var entity = await ResolveAccountAsync(app, externalAccountId, ct);

        var service = new AccountService();
        Account stripeAccount;
        try
        {
            stripeAccount = await service.GetAsync(entity.StripeAccountId, cancellationToken: ct);
        }
        catch (StripeException ex)
        {
            throw new InvalidOperationException(ex.Message);
        }

        ApplyStripeAccount(entity, stripeAccount);
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task SyncAccountFromStripeAsync(Account stripeAccount, CancellationToken ct = default)
    {
        var entity = await db.ConnectedAccounts
            .Include(x => x.ClientApplication)
            .FirstOrDefaultAsync(x => x.StripeAccountId == stripeAccount.Id, ct);
        if (entity is null)
            return;

        ApplyStripeAccount(entity, stripeAccount);
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<ConnectedAccount> ResolveAccountAsync(ClientApplication app, string externalAccountId, CancellationToken ct)
    {
        var account = await db.ConnectedAccounts
            .FirstOrDefaultAsync(x =>
                x.ClientApplicationId == app.Id
                && (x.StripeAccountId == externalAccountId || x.ExternalReference == externalAccountId), ct);

        return account ?? throw new InvalidOperationException("Compte Connect introuvable.");
    }

    private static void ApplyStripeAccount(ConnectedAccount entity, Account account)
    {
        entity.ChargesEnabled = account.ChargesEnabled;
        entity.PayoutsEnabled = account.PayoutsEnabled;
        entity.DetailsSubmitted = account.DetailsSubmitted;
        entity.Status = DeriveStatus(account);
        entity.RequirementsCurrentlyDueJson = JsonSerializer.Serialize(account.Requirements?.CurrentlyDue ?? []);
        entity.LastSyncedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(account.Email))
            entity.Email = account.Email;
        if (!string.IsNullOrWhiteSpace(account.Country))
            entity.Country = account.Country.ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(account.DefaultCurrency))
            entity.Currency = account.DefaultCurrency.ToLowerInvariant();
    }

    private static string DeriveStatus(Account account)
    {
        if (account.PayoutsEnabled && account.DetailsSubmitted)
            return "verified";
        if (account.Requirements?.CurrentlyDue?.Count > 0)
            return "action_required";
        if (account.DetailsSubmitted)
            return "pending_verification";
        return "incomplete";
    }

    private static ConnectAccountResponse Map(ConnectedAccount e) => new(
        e.StripeAccountId,
        e.ExternalReference,
        e.Country,
        e.Currency,
        MaskEmail(e.Email),
        e.Status,
        e.ChargesEnabled,
        e.PayoutsEnabled,
        e.DetailsSubmitted,
        e.RequirementsCurrentlyDueJson);

    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return email;
        var parts = email.Split('@', 2);
        var local = parts[0];
        var maskedLocal = local.Length <= 2
            ? new string('•', local.Length)
            : $"{local[0]}{new string('•', Math.Min(local.Length - 2, 6))}{local[^1]}";
        return $"{maskedLocal}@{parts[1]}";
    }
}

public class ConnectTransferService(
    ApplicationDbContext db,
    IStripeSettingsProvider stripeSettings,
    IAuditService auditService,
    ILogger<ConnectTransferService> logger) : ITransferService
{
    private async Task ConfigureStripeAsync(CancellationToken cancellationToken)
    {
        var settings = await stripeSettings.GetActiveAsync(cancellationToken)
            ?? throw new InvalidOperationException("Stripe n'est pas configuré.");
        if (string.IsNullOrWhiteSpace(settings.SecretKey))
            throw new InvalidOperationException("Stripe n'est pas configuré.");
        StripeConfiguration.ApiKey = settings.SecretKey;
    }

    public async Task<ConnectTransferResponse> CreateTransferAsync(
        ClientApplication app,
        CreateConnectTransferRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new InvalidOperationException("IdempotencyKey obligatoire.");

        var existing = await db.ConnectTransfers
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.ClientApplicationId == app.Id
                && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
            return Map(existing);

        await ConfigureStripeAsync(ct);

        var destination = request.DestinationAccountId;
        var connected = await db.ConnectedAccounts
            .FirstOrDefaultAsync(x =>
                x.ClientApplicationId == app.Id
                && (x.StripeAccountId == destination || x.ExternalReference == destination), ct);
        if (connected is not null)
            destination = connected.StripeAccountId;

        var currency = (request.Currency ?? "cad").Trim().ToLowerInvariant();
        var stripeTransfers = new TransferService();
        Transfer transfer;
        try
        {
            transfer = await stripeTransfers.CreateAsync(new TransferCreateOptions
            {
                Amount = request.AmountMinor,
                Currency = currency,
                Destination = destination,
                Description = request.Description,
                Metadata = request.Metadata ?? new Dictionary<string, string>
                {
                    ["app_code"] = app.AppCode,
                    ["idempotency_key"] = request.IdempotencyKey
                }
            }, new RequestOptions { IdempotencyKey = request.IdempotencyKey }, ct);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Échec transfert Connect {Key}", request.IdempotencyKey);
            throw new InvalidOperationException(ex.Message);
        }

        var entity = new ConnectTransfer
        {
            ClientApplicationId = app.Id,
            IdempotencyKey = request.IdempotencyKey,
            StripeTransferId = transfer.Id,
            DestinationAccountId = destination,
            AmountMinor = request.AmountMinor,
            Currency = currency,
            Status = "submitted",
            Description = request.Description,
            MetadataJson = request.Metadata is null ? null : JsonSerializer.Serialize(request.Metadata)
        };

        db.ConnectTransfers.Add(entity);
        await db.SaveChangesAsync(ct);
        await auditService.LogAsync("ConnectTransferCreated", nameof(ConnectTransfer), entity.Id.ToString(), true, transfer.Id, app.AppCode);

        return Map(entity);
    }

    public async Task<ConnectTransferResponse?> GetTransferAsync(ClientApplication app, string transferId, CancellationToken ct = default)
    {
        var entity = await db.ConnectTransfers
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.ClientApplicationId == app.Id
                && (x.StripeTransferId == transferId || x.IdempotencyKey == transferId), ct);
        return entity is null ? null : Map(entity);
    }

    public async Task UpdateTransferStatusAsync(
        string stripeTransferId,
        string status,
        string? failureCode,
        string? failureMessage,
        CancellationToken ct = default)
    {
        var entity = await db.ConnectTransfers
            .FirstOrDefaultAsync(x => x.StripeTransferId == stripeTransferId, ct);
        if (entity is null)
            return;

        entity.Status = status;
        entity.FailureCode = failureCode;
        entity.FailureMessage = failureMessage;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static ConnectTransferResponse Map(ConnectTransfer e) => new(
        e.StripeTransferId,
        e.IdempotencyKey,
        e.DestinationAccountId,
        e.AmountMinor,
        e.Currency,
        e.Status,
        e.FailureCode,
        e.FailureMessage);
}
