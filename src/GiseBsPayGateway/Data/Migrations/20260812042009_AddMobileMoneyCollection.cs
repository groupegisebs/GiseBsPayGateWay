using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiseBsPayGateway.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileMoneyCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_ClientApplicationId",
                table: "PaymentTransactions");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "PaymentTransactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                table: "PaymentTransactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "PaymentTransactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MobileMoneyChannel",
                table: "PaymentTransactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneMasked",
                table: "PaymentTransactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderReference",
                table: "PaymentTransactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawProviderStatus",
                table: "PaymentTransactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MobileMoneyWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ProcessingResult = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileMoneyWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_ClientApplicationId_IdempotencyKey",
                table: "PaymentTransactions",
                columns: new[] { "ClientApplicationId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_ProviderReference",
                table: "PaymentTransactions",
                column: "ProviderReference");

            migrationBuilder.CreateIndex(
                name: "IX_MobileMoneyWebhookEvents_PayloadHash",
                table: "MobileMoneyWebhookEvents",
                column: "PayloadHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MobileMoneyWebhookEvents_Provider_ProviderEventId",
                table: "MobileMoneyWebhookEvents",
                columns: new[] { "Provider", "ProviderEventId" },
                unique: true,
                filter: "\"ProviderEventId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MobileMoneyWebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_ClientApplicationId_IdempotencyKey",
                table: "PaymentTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_ProviderReference",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "FailureCode",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "MobileMoneyChannel",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "PhoneMasked",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "ProviderReference",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RawProviderStatus",
                table: "PaymentTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_ClientApplicationId",
                table: "PaymentTransactions",
                column: "ClientApplicationId");
        }
    }
}
