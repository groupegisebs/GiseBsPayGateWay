using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiseBsPayGateway.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260713120000_AddConnectAccountsAndTransfers")]
public partial class AddConnectAccountsAndTransfers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ConnectedAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                StripeAccountId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                AccountType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ChargesEnabled = table.Column<bool>(type: "boolean", nullable: false),
                PayoutsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                DetailsSubmitted = table.Column<bool>(type: "boolean", nullable: false),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                RequirementsCurrentlyDueJson = table.Column<string>(type: "text", nullable: true),
                LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConnectedAccounts", x => x.Id);
                table.ForeignKey(
                    name: "FK_ConnectedAccounts_ClientApplications_ClientApplicationId",
                    column: x => x.ClientApplicationId,
                    principalTable: "ClientApplications",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ConnectTransfers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClientApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                StripeTransferId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                DestinationAccountId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                FailureMessage = table.Column<string>(type: "text", nullable: true),
                MetadataJson = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConnectTransfers", x => x.Id);
                table.ForeignKey(
                    name: "FK_ConnectTransfers_ClientApplications_ClientApplicationId",
                    column: x => x.ClientApplicationId,
                    principalTable: "ClientApplications",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ConnectedAccounts_ClientApplicationId_ExternalReference",
            table: "ConnectedAccounts",
            columns: new[] { "ClientApplicationId", "ExternalReference" });

        migrationBuilder.CreateIndex(
            name: "IX_ConnectedAccounts_StripeAccountId",
            table: "ConnectedAccounts",
            column: "StripeAccountId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ConnectTransfers_ClientApplicationId_IdempotencyKey",
            table: "ConnectTransfers",
            columns: new[] { "ClientApplicationId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ConnectTransfers_StripeTransferId",
            table: "ConnectTransfers",
            column: "StripeTransferId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ConnectTransfers");
        migrationBuilder.DropTable(name: "ConnectedAccounts");
    }
}
