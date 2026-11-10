using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "Outgoings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OwnerPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParentAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginallyRecordedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OriginallyRecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_Accounts_ParentAccountId",
                        column: x => x.ParentAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Accounts_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Accounts_People_OwnerPersonId",
                        column: x => x.OwnerPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Outgoings_AccountId",
                table: "Outgoings",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_HouseholdId_ParentAccountId_Name",
                table: "Accounts",
                columns: new[] { "HouseholdId", "ParentAccountId", "Name" },
                unique: true,
                filter: "[ParentAccountId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_OwnerPersonId",
                table: "Accounts",
                column: "OwnerPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentAccountId",
                table: "Accounts",
                column: "ParentAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Outgoings_Accounts_AccountId",
                table: "Outgoings",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Each household's "Paid from" names become accounts (ADR-014, BUD-89), ignoring case and spaces at
            // either end, and "Y (X Pot)" becomes pot X in account Y (the last bracket, as AccountNames.Parse reads it).
            // SELECT INTO keeps the column's collation, so the comparisons ignore case like the rest of the database.
            // Where spellings differ only in case, the binary minimum keeps the one with capitals, e.g. "Joint bills".
            migrationBuilder.Sql("""
                SELECT o.Id AS OutgoingId, o.HouseholdId,
                    CASE WHEN n.Name LIKE N'%_(%_ pot)' THEN LTRIM(RTRIM(LEFT(n.Name, b.Position - 1))) ELSE n.Name END AS AccountName,
                    CASE WHEN n.Name LIKE N'%_(%_ pot)' THEN LTRIM(RTRIM(SUBSTRING(n.Name, b.Position + 1, LEN(n.Name) - b.Position - 4))) END AS PotName
                INTO #PaidFrom
                FROM Outgoings o
                CROSS APPLY (SELECT LTRIM(RTRIM(o.PaidFrom)) AS Name) n
                CROSS APPLY (SELECT LEN(n.Name) - CHARINDEX(N'(', REVERSE(n.Name)) + 1 AS Position) b
                WHERE o.PaidFrom IS NOT NULL AND n.Name <> N'';

                INSERT INTO Accounts (Id, HouseholdId, Name, Provider, OwnerPersonId, ParentAccountId, IsArchived, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
                SELECT NEWID(), HouseholdId, MIN(AccountName COLLATE Latin1_General_BIN2), NULL, NULL, NULL, 0, SYSDATETIMEOFFSET(), NULL, SYSDATETIMEOFFSET(), NULL
                FROM #PaidFrom
                GROUP BY HouseholdId, AccountName;

                INSERT INTO Accounts (Id, HouseholdId, Name, Provider, OwnerPersonId, ParentAccountId, IsArchived, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
                SELECT NEWID(), p.HouseholdId, MIN(p.PotName COLLATE Latin1_General_BIN2), NULL, NULL, a.Id, 0, SYSDATETIMEOFFSET(), NULL, SYSDATETIMEOFFSET(), NULL
                FROM #PaidFrom p
                JOIN Accounts a ON a.HouseholdId = p.HouseholdId AND a.ParentAccountId IS NULL AND a.Name = p.AccountName
                WHERE p.PotName IS NOT NULL AND p.PotName <> N''
                GROUP BY p.HouseholdId, a.Id, p.PotName;

                UPDATE o SET AccountId = COALESCE(pot.Id, account.Id)
                FROM Outgoings o
                JOIN #PaidFrom p ON p.OutgoingId = o.Id
                JOIN Accounts account ON account.HouseholdId = p.HouseholdId AND account.ParentAccountId IS NULL AND account.Name = p.AccountName
                LEFT JOIN Accounts pot ON pot.ParentAccountId = account.Id AND pot.Name = p.PotName;

                DROP TABLE #PaidFrom;
                """);

            migrationBuilder.DropColumn(
                name: "PaidFrom",
                table: "Outgoings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaidFrom",
                table: "Outgoings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // Back to text: "Account", or "Account (Pot Pot)" as it would have been typed.
            migrationBuilder.Sql("""
                UPDATE o SET PaidFrom = LEFT(CASE WHEN parent.Id IS NULL THEN a.Name ELSE parent.Name + N' (' + a.Name + N' Pot)' END, 100)
                FROM Outgoings o
                JOIN Accounts a ON a.Id = o.AccountId
                LEFT JOIN Accounts parent ON parent.Id = a.ParentAccountId;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Outgoings_Accounts_AccountId",
                table: "Outgoings");

            migrationBuilder.DropIndex(
                name: "IX_Outgoings_AccountId",
                table: "Outgoings");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Outgoings");

            migrationBuilder.Sql("UPDATE Accounts SET ParentAccountId = NULL;");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
