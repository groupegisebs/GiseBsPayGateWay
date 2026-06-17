using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiseBsPayGateway.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeFeeAndTaxFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountSubtotal",
                table: "PaymentTransactions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCountry",
                table: "PaymentTransactions",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingState",
                table: "PaymentTransactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossAmount",
                table: "PaymentTransactions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "PaymentTransactions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeBalanceTransactionId",
                table: "PaymentTransactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StripeFee",
                table: "PaymentTransactions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "PaymentTransactions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountSubtotal",
                table: "PaymentInvoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCountry",
                table: "PaymentInvoices",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingState",
                table: "PaymentInvoices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossAmount",
                table: "PaymentInvoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "PaymentInvoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeBalanceTransactionId",
                table: "PaymentInvoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StripeFee",
                table: "PaymentInvoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "PaymentInvoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountSubtotal",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "BillingCountry",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "BillingState",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "GrossAmount",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "StripeBalanceTransactionId",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "StripeFee",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "AmountSubtotal",
                table: "PaymentInvoices");

            migrationBuilder.DropColumn(
                name: "BillingCountry",
                table: "PaymentInvoices");

            migrationBuilder.DropColumn(
                name: "BillingState",
                table: "PaymentInvoices");

            migrationBuilder.DropColumn(
                name: "GrossAmount",
                table: "PaymentInvoices");

            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "PaymentInvoices");

            migrationBuilder.DropColumn(
                name: "StripeBalanceTransactionId",
                table: "PaymentInvoices");

            migrationBuilder.DropColumn(
                name: "StripeFee",
                table: "PaymentInvoices");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "PaymentInvoices");
        }
    }
}
