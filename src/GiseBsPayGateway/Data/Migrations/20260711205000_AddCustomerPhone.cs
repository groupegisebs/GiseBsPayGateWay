using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GiseBsPayGateway.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260711205000_AddCustomerPhone")]
public partial class AddCustomerPhone : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Phone",
            table: "Customers",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "FullName",
            table: "Customers",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Phone", table: "Customers");

        migrationBuilder.AlterColumn<string>(
            name: "FullName",
            table: "Customers",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200,
            oldNullable: true);
    }
}
