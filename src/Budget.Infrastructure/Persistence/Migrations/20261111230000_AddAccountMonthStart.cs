using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountMonthStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // When an account's month starts (BUD-136): the income or transfer whose arrival starts it, or null for the 1st.
            migrationBuilder.AddColumn<Guid>(
                name: "MonthStartsWithId",
                table: "Accounts",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonthStartsWithId",
                table: "Accounts");
        }
    }
}
