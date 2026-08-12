using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiseBsPayGateway.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFlutterwave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent : certaines bases n'ont jamais reçu AddFlutterwave* (ou schema diverge de __EFMigrationsHistory).
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS "FlutterwaveWebhookEvents";
                DROP INDEX IF EXISTS "IX_PaymentTransactions_FlutterwaveTxRef";
                ALTER TABLE "PaymentTransactions" DROP COLUMN IF EXISTS "FlutterwaveTransactionId";
                ALTER TABLE "PaymentTransactions" DROP COLUMN IF EXISTS "FlutterwaveTxRef";
                ALTER TABLE "PaymentTransactions" DROP COLUMN IF EXISTS "MobileMoneyCountry";
                ALTER TABLE "PaymentTransactions" DROP COLUMN IF EXISTS "MobileMoneyNetwork";
                ALTER TABLE "PaymentTransactions" DROP COLUMN IF EXISTS "MobileMoneyPhone";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FlutterwaveTransactionId",
                table: "PaymentTransactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlutterwaveTxRef",
                table: "PaymentTransactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MobileMoneyCountry",
                table: "PaymentTransactions",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MobileMoneyNetwork",
                table: "PaymentTransactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MobileMoneyPhone",
                table: "PaymentTransactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FlutterwaveWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargeId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FlutterwaveEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlutterwaveWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_FlutterwaveTxRef",
                table: "PaymentTransactions",
                column: "FlutterwaveTxRef");

            migrationBuilder.CreateIndex(
                name: "IX_FlutterwaveWebhookEvents_FlutterwaveEventId",
                table: "FlutterwaveWebhookEvents",
                column: "FlutterwaveEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlutterwaveWebhookEvents_Reference",
                table: "FlutterwaveWebhookEvents",
                column: "Reference");
        }
    }
}
