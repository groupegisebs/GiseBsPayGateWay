using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiseBsPayGateway.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFlutterwaveMobileMoney : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "PaymentTransactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_FlutterwaveTxRef",
                table: "PaymentTransactions",
                column: "FlutterwaveTxRef");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "PaymentTransactions");
        }
    }
}
