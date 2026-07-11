using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiseBsPayGateway.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260711192000_PricingPlanUniqueByCurrency")]
public partial class PricingPlanUniqueByCurrency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PricingPlans_ProductId_PlanCode",
            table: "PricingPlans");

        migrationBuilder.CreateIndex(
            name: "IX_PricingPlans_ProductId_PlanCode_Currency",
            table: "PricingPlans",
            columns: new[] { "ProductId", "PlanCode", "Currency" },
            unique: true,
            filter: "\"IsActive\" = true");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PricingPlans_ProductId_PlanCode_Currency",
            table: "PricingPlans");

        migrationBuilder.CreateIndex(
            name: "IX_PricingPlans_ProductId_PlanCode",
            table: "PricingPlans",
            columns: new[] { "ProductId", "PlanCode" },
            unique: true,
            filter: "\"IsActive\" = true");
    }
}
