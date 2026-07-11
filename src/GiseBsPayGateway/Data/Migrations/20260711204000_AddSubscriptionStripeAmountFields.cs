using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiseBsPayGateway.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260711204000_AddSubscriptionStripeAmountFields")]
public partial class AddSubscriptionStripeAmountFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "StripeAmount",
            table: "Subscriptions",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StripeCurrency",
            table: "Subscriptions",
            type: "character varying(3)",
            maxLength: 3,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "StripeSyncedAt",
            table: "Subscriptions",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StripeAmount", table: "Subscriptions");
        migrationBuilder.DropColumn(name: "StripeCurrency", table: "Subscriptions");
        migrationBuilder.DropColumn(name: "StripeSyncedAt", table: "Subscriptions");
    }
}
