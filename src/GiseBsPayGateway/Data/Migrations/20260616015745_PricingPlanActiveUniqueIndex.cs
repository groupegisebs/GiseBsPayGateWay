using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiseBsPayGateway.Data.Migrations
{
    /// <inheritdoc />
    public partial class PricingPlanActiveUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PricingPlans_ProductId_PlanCode",
                table: "PricingPlans");

            migrationBuilder.CreateIndex(
                name: "IX_PricingPlans_ProductId_PlanCode",
                table: "PricingPlans",
                columns: new[] { "ProductId", "PlanCode" },
                unique: true,
                filter: "\"IsActive\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PricingPlans_ProductId_PlanCode",
                table: "PricingPlans");

            migrationBuilder.CreateIndex(
                name: "IX_PricingPlans_ProductId_PlanCode",
                table: "PricingPlans",
                columns: new[] { "ProductId", "PlanCode" },
                unique: true);
        }
    }
}
