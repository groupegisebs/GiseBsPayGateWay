using Microsoft.EntityFrameworkCore;

namespace GiseBsPayGateway.Data;

/// <summary>
/// Filet schéma idempotent. Certaines bases ont __EFMigrationsHistory à jour
/// alors que des colonnes (ex. PaymentTransactions.Provider) n'existent pas —
/// Postgres 42703, page admin /Error.
/// </summary>
internal static class SchemaFilet
{
    public static async Task EnsureAsync(ApplicationDbContext db, ILogger logger, CancellationToken ct = default)
    {
        await RunAsync(db, logger, "Provider",
            """ALTER TABLE "PaymentTransactions" ADD COLUMN IF NOT EXISTS "Provider" character varying(30) NOT NULL DEFAULT 'stripe';""",
            ct);
        await RunAsync(db, logger, "Provider backfill",
            """UPDATE "PaymentTransactions" SET "Provider" = 'stripe' WHERE "Provider" IS NULL OR btrim("Provider") = '';""",
            ct);

        await RunAsync(db, logger, "MobileMoneyChannel",
            """ALTER TABLE "PaymentTransactions" ADD COLUMN IF NOT EXISTS "MobileMoneyChannel" character varying(20);""",
            ct);
        await RunAsync(db, logger, "PhoneMasked",
            """ALTER TABLE "PaymentTransactions" ADD COLUMN IF NOT EXISTS "PhoneMasked" character varying(40);""",
            ct);
        await RunAsync(db, logger, "ProviderReference",
            """ALTER TABLE "PaymentTransactions" ADD COLUMN IF NOT EXISTS "ProviderReference" character varying(100);""",
            ct);
        await RunAsync(db, logger, "IdempotencyKey",
            """ALTER TABLE "PaymentTransactions" ADD COLUMN IF NOT EXISTS "IdempotencyKey" character varying(100);""",
            ct);
        await RunAsync(db, logger, "ExpiresAtUtc",
            """ALTER TABLE "PaymentTransactions" ADD COLUMN IF NOT EXISTS "ExpiresAtUtc" timestamp with time zone;""",
            ct);
        await RunAsync(db, logger, "FailureCode",
            """ALTER TABLE "PaymentTransactions" ADD COLUMN IF NOT EXISTS "FailureCode" character varying(50);""",
            ct);
        await RunAsync(db, logger, "RawProviderStatus",
            """ALTER TABLE "PaymentTransactions" ADD COLUMN IF NOT EXISTS "RawProviderStatus" character varying(50);""",
            ct);
        await RunAsync(db, logger, "BillingCountry",
            """ALTER TABLE "PaymentTransactions" ADD COLUMN IF NOT EXISTS "BillingCountry" character varying(2);""",
            ct);
        await RunAsync(db, logger, "BillingState",
            """ALTER TABLE "PaymentTransactions" ADD COLUMN IF NOT EXISTS "BillingState" character varying(50);""",
            ct);

        await RunAsync(db, logger, "IX_PaymentTransactions_ProviderReference",
            """CREATE INDEX IF NOT EXISTS "IX_PaymentTransactions_ProviderReference" ON "PaymentTransactions" ("ProviderReference");""",
            ct);
        await RunAsync(db, logger, "IX_PaymentTransactions_ClientApplicationId_IdempotencyKey",
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PaymentTransactions_ClientApplicationId_IdempotencyKey"
            ON "PaymentTransactions" ("ClientApplicationId", "IdempotencyKey")
            WHERE "IdempotencyKey" IS NOT NULL;
            """,
            ct);

        await RunAsync(db, logger, "MobileMoneyWebhookEvents",
            """
            CREATE TABLE IF NOT EXISTS "MobileMoneyWebhookEvents" (
                "Id" uuid NOT NULL,
                "Provider" character varying(30) NOT NULL,
                "ProviderEventId" character varying(100),
                "EventType" character varying(100) NOT NULL,
                "PayloadHash" character varying(64) NOT NULL,
                "ProcessingStatus" integer NOT NULL,
                "Payload" text,
                "ErrorMessage" text,
                "ProcessingResult" character varying(100),
                "ProcessedAt" timestamp with time zone,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone,
                CONSTRAINT "PK_MobileMoneyWebhookEvents" PRIMARY KEY ("Id")
            );
            """,
            ct);
        await RunAsync(db, logger, "IX_MobileMoneyWebhookEvents_PayloadHash",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_MobileMoneyWebhookEvents_PayloadHash" ON "MobileMoneyWebhookEvents" ("PayloadHash");""",
            ct);
        await RunAsync(db, logger, "IX_MobileMoneyWebhookEvents_Provider_ProviderEventId",
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_MobileMoneyWebhookEvents_Provider_ProviderEventId"
            ON "MobileMoneyWebhookEvents" ("Provider", "ProviderEventId")
            WHERE "ProviderEventId" IS NOT NULL;
            """,
            ct);

        await RunAsync(db, logger, "AfricanTaxRateSettings",
            """
            CREATE TABLE IF NOT EXISTS "AfricanTaxRateSettings" (
                "Id" uuid NOT NULL,
                "CountryCode" character varying(2) NOT NULL,
                "CountryNameFr" character varying(100) NOT NULL,
                "TaxName" character varying(40) NOT NULL,
                "RatePercent" numeric(9,4) NOT NULL,
                "StandardRatePercent" numeric(9,4) NOT NULL,
                "Notes" character varying(500),
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone,
                CONSTRAINT "PK_AfricanTaxRateSettings" PRIMARY KEY ("Id")
            );
            """,
            ct);
        await RunAsync(db, logger, "IX_AfricanTaxRateSettings_CountryCode",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_AfricanTaxRateSettings_CountryCode" ON "AfricanTaxRateSettings" ("CountryCode");""",
            ct);

        logger.LogInformation("Schema filet Pay Gateway terminé (Provider / Mobile Money / taux Afrique).");
    }

    private static async Task RunAsync(
        ApplicationDbContext db,
        ILogger logger,
        string name,
        string sql,
        CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Schema filet « {Name} » ignoré (déjà présent ou non applicable).", name);
        }
    }
}
