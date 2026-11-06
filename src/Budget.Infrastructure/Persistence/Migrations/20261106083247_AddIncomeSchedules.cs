using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomeSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MonthlyNetAmount",
                table: "IncomeAmountChanges",
                newName: "Amount");

            migrationBuilder.AddColumn<int>(
                name: "Every",
                table: "IncomeAmountChanges",
                type: "int",
                nullable: false,
                defaultValue: 1); // Existing income was always monthly (BUD-57).

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "IncomeAmountChanges",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Month");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Every",
                table: "IncomeAmountChanges");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "IncomeAmountChanges");

            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "IncomeAmountChanges",
                newName: "MonthlyNetAmount");
        }
    }
}
