using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomeAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "IncomeStreams",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomeStreams_AccountId",
                table: "IncomeStreams",
                column: "AccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_IncomeStreams_Accounts_AccountId",
                table: "IncomeStreams",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IncomeStreams_Accounts_AccountId",
                table: "IncomeStreams");

            migrationBuilder.DropIndex(
                name: "IX_IncomeStreams_AccountId",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "IncomeStreams");
        }
    }
}
