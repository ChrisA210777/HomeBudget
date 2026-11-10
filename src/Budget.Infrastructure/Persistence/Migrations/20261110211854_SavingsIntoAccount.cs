using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SavingsIntoAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SavingsAccountId",
                table: "Outgoings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outgoings_SavingsAccountId",
                table: "Outgoings",
                column: "SavingsAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Outgoings_Accounts_SavingsAccountId",
                table: "Outgoings",
                column: "SavingsAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Outgoings_Accounts_SavingsAccountId",
                table: "Outgoings");

            migrationBuilder.DropIndex(
                name: "IX_Outgoings_SavingsAccountId",
                table: "Outgoings");

            migrationBuilder.DropColumn(
                name: "SavingsAccountId",
                table: "Outgoings");
        }
    }
}
