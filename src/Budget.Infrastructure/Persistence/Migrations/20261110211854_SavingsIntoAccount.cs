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

            // Each savings outgoing's typed destination becomes an account, or a pot for "Account (Pot Pot)", as
            // "Paid from" did (ADR-014, BUD-89). Names match existing accounts and pots ignoring case.
            migrationBuilder.Sql("""
                SELECT o.Id AS OutgoingId, o.HouseholdId,
                    CASE WHEN n.Name LIKE N'%_(%_ pot)' THEN LTRIM(RTRIM(LEFT(n.Name, b.Position - 1))) ELSE n.Name END AS AccountName,
                    CASE WHEN n.Name LIKE N'%_(%_ pot)' THEN LTRIM(RTRIM(SUBSTRING(n.Name, b.Position + 1, LEN(n.Name) - b.Position - 4))) END AS PotName
                INTO #SavedInto
                FROM Outgoings o
                CROSS APPLY (SELECT LTRIM(RTRIM(o.Recipient)) AS Name) n
                CROSS APPLY (SELECT LEN(n.Name) - CHARINDEX(N'(', REVERSE(n.Name)) + 1 AS Position) b
                WHERE o.Kind = N'Savings' AND o.Recipient IS NOT NULL AND n.Name <> N'';

                INSERT INTO Accounts (Id, HouseholdId, Name, Provider, OwnerPersonId, ParentAccountId, IsArchived, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
                SELECT NEWID(), s.HouseholdId, MIN(s.AccountName COLLATE Latin1_General_BIN2), NULL, NULL, NULL, 0, SYSDATETIMEOFFSET(), NULL, SYSDATETIMEOFFSET(), NULL
                FROM #SavedInto s
                WHERE NOT EXISTS (SELECT 1 FROM Accounts a WHERE a.HouseholdId = s.HouseholdId AND a.ParentAccountId IS NULL AND a.Name = s.AccountName)
                GROUP BY s.HouseholdId, s.AccountName;

                INSERT INTO Accounts (Id, HouseholdId, Name, Provider, OwnerPersonId, ParentAccountId, IsArchived, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
                SELECT NEWID(), s.HouseholdId, MIN(s.PotName COLLATE Latin1_General_BIN2), NULL, NULL, a.Id, 0, SYSDATETIMEOFFSET(), NULL, SYSDATETIMEOFFSET(), NULL
                FROM #SavedInto s
                JOIN Accounts a ON a.HouseholdId = s.HouseholdId AND a.ParentAccountId IS NULL AND a.Name = s.AccountName
                WHERE s.PotName IS NOT NULL AND s.PotName <> N''
                    AND NOT EXISTS (SELECT 1 FROM Accounts p WHERE p.ParentAccountId = a.Id AND p.Name = s.PotName)
                GROUP BY s.HouseholdId, a.Id, s.PotName;

                UPDATE o SET SavingsAccountId = COALESCE(pot.Id, account.Id), Recipient = NULL
                FROM Outgoings o
                JOIN #SavedInto s ON s.OutgoingId = o.Id
                JOIN Accounts account ON account.HouseholdId = s.HouseholdId AND account.ParentAccountId IS NULL AND account.Name = s.AccountName
                LEFT JOIN Accounts pot ON pot.ParentAccountId = account.Id AND pot.Name = s.PotName;

                DROP TABLE #SavedInto;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to text: the account, or "Account (Pot Pot)".
            migrationBuilder.Sql("""
                UPDATE o SET Recipient = LEFT(CASE WHEN parent.Id IS NULL THEN a.Name ELSE parent.Name + N' (' + a.Name + N' Pot)' END, 100)
                FROM Outgoings o
                JOIN Accounts a ON a.Id = o.SavingsAccountId
                LEFT JOIN Accounts parent ON parent.Id = a.ParentAccountId;
                """);

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
