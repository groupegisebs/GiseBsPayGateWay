using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiseBsPayGateway.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260713140000_AddDisbursementQueueAndPayPalMm")]
public partial class AddDisbursementQueueAndPayPalMm : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SellerDisbursementRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientApplicationIdLegacy = table.Column<int>(type: "integer", nullable: true),
                ClientApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                ExternalReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SellerExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SellerDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Channel = table.Column<int>(type: "integer", nullable: false),
                ProviderCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                DestinationMasked = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                DestinationToken = table.Column<string>(type: "text", nullable: true),
                AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ReconciliationNotes = table.Column<string>(type: "text", nullable: true),
                ReconciliationChecked = table.Column<bool>(type: "boolean", nullable: false),
                ReviewedByUserId = table.Column<string>(type: "text", nullable: true),
                ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RejectionReason = table.Column<string>(type: "text", nullable: true),
                ProviderPayoutId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                FailureCode = table.Column<string>(type: "text", nullable: true),
                FailureMessage = table.Column<string>(type: "text", nullable: true),
                MetadataJson = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SellerDisbursementRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_SellerDisbursementRequests_ClientApplications_ClientApplicationId",
                    column: x => x.ClientApplicationId,
                    principalTable: "ClientApplications",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PayPalLinkedAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                PayerId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                MaskedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                RefreshTokenEncrypted = table.Column<string>(type: "text", nullable: true),
                AccessTokenEncrypted = table.Column<string>(type: "text", nullable: true),
                AccessTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                LastVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PayPalLinkedAccounts", x => x.Id);
                table.ForeignKey(
                    name: "FK_PayPalLinkedAccounts_ClientApplications_ClientApplicationId",
                    column: x => x.ClientApplicationId,
                    principalTable: "ClientApplications",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MobileMoneyRecipients",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                OperatorCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                AccountHolderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PhoneE164 = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                MaskedPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                PublicAccountId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MobileMoneyRecipients", x => x.Id);
                table.ForeignKey(
                    name: "FK_MobileMoneyRecipients_ClientApplications_ClientApplicationId",
                    column: x => x.ClientApplicationId,
                    principalTable: "ClientApplications",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SellerDisbursementRequests_ClientApplicationId_IdempotencyKey",
            table: "SellerDisbursementRequests",
            columns: new[] { "ClientApplicationId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SellerDisbursementRequests_Status",
            table: "SellerDisbursementRequests",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_PayPalLinkedAccounts_ClientApplicationId_ExternalReference",
            table: "PayPalLinkedAccounts",
            columns: new[] { "ClientApplicationId", "ExternalReference" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MobileMoneyRecipients_ClientApplicationId_ExternalReference",
            table: "MobileMoneyRecipients",
            columns: new[] { "ClientApplicationId", "ExternalReference" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SellerDisbursementRequests");
        migrationBuilder.DropTable(name: "PayPalLinkedAccounts");
        migrationBuilder.DropTable(name: "MobileMoneyRecipients");
    }
}
