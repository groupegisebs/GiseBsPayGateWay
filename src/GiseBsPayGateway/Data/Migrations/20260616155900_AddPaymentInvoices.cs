using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiseBsPayGateway.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ClientApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PricingPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CustomerEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProductCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PlanName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    BillingReason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    InvoiceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StripeInvoiceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StripeInvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StripeCheckoutSessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HostedInvoiceUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InvoicePdfUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReceiptUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LineItemsDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentInvoices_ClientApplications_ClientApplicationId",
                        column: x => x.ClientApplicationId,
                        principalTable: "ClientApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentInvoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentInvoices_PaymentTransactions_PaymentTransactionId",
                        column: x => x.PaymentTransactionId,
                        principalTable: "PaymentTransactions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentInvoices_PricingPlans_PricingPlanId",
                        column: x => x.PricingPlanId,
                        principalTable: "PricingPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentInvoices_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentInvoices_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInvoices_ClientApplicationId",
                table: "PaymentInvoices",
                column: "ClientApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInvoices_CustomerId",
                table: "PaymentInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInvoices_InvoiceCode",
                table: "PaymentInvoices",
                column: "InvoiceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInvoices_PaymentTransactionId",
                table: "PaymentInvoices",
                column: "PaymentTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInvoices_PricingPlanId",
                table: "PaymentInvoices",
                column: "PricingPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInvoices_ProductId",
                table: "PaymentInvoices",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInvoices_StripeInvoiceId",
                table: "PaymentInvoices",
                column: "StripeInvoiceId",
                unique: true,
                filter: "\"StripeInvoiceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInvoices_SubscriptionId",
                table: "PaymentInvoices",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentInvoices");
        }
    }
}
