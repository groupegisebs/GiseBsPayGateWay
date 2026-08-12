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
            migrationBuilder.DropTable(
                name: "FlutterwaveWebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_FlutterwaveTxRef",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "FlutterwaveTransactionId",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "FlutterwaveTxRef",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "MobileMoneyCountry",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "MobileMoneyNetwork",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "MobileMoneyPhone",
                table: "PaymentTransactions");
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
