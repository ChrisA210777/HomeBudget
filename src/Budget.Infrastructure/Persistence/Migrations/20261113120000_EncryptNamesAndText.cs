using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncryptNamesAndText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Names and free text are stored encrypted (BUD-166, ADR-017). The values themselves are encrypted by the app when
            // it starts (EncryptExistingData), because only the app has the key. Encrypted text is longer, so columns widen;
            // uniqueness moves from the names to a keyed hash of them (NameKey), filled in by the app too.

            migrationBuilder.DropIndex(
                name: "IX_People_HouseholdId_DisplayName",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_HouseholdId_ParentAccountId_Name",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingCategories_HouseholdId_Name",
                table: "OutgoingCategories");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Accounts",
                type: "nvarchar(454)",
                maxLength: 454,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "Accounts",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                table: "Accounts",
                type: "nvarchar(454)",
                maxLength: 454,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ActorName",
                table: "ActivityEntries",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "Summary",
                table: "ActivityEntries",
                type: "nvarchar(1254)",
                maxLength: 1254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Households",
                type: "nvarchar(454)",
                maxLength: 454,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "Households",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "HouseholdMembers",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "Invitations",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "IncomeAmountChanges",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "IncomeAmountChanges",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "After",
                table: "IncomeCorrections",
                type: "nvarchar(2454)",
                maxLength: 2454,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Before",
                table: "IncomeCorrections",
                type: "nvarchar(2454)",
                maxLength: 2454,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "IncomeCorrections",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "IncomeCorrections",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "IncomeStatusPeriods",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "IncomeStatusPeriods",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "IncomeStreams",
                type: "nvarchar(454)",
                maxLength: 454,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "IncomeStreams",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContactPhone",
                table: "Outgoings",
                type: "nvarchar(174)",
                maxLength: 174,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Outgoings",
                type: "nvarchar(454)",
                maxLength: 454,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "Outgoings",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "Outgoings",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Recipient",
                table: "Outgoings",
                type: "nvarchar(454)",
                maxLength: 454,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceLabel",
                table: "Outgoings",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceValue",
                table: "Outgoings",
                type: "nvarchar(454)",
                maxLength: 454,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Website",
                table: "Outgoings",
                type: "nvarchar(1254)",
                maxLength: 1254,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "OutgoingAmountChanges",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "OutgoingAmountChanges",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "OutgoingCategories",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "OutgoingCategories",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "After",
                table: "OutgoingCorrections",
                type: "nvarchar(2454)",
                maxLength: 2454,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Before",
                table: "OutgoingCorrections",
                type: "nvarchar(2454)",
                maxLength: 2454,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "OutgoingCorrections",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "OutgoingCorrections",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "OutgoingStatusPeriods",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "OutgoingStatusPeriods",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "People",
                type: "nvarchar(454)",
                maxLength: 454,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "People",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Transfers",
                type: "nvarchar(454)",
                maxLength: 454,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "Transfers",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "Transfers",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "TransferAmountChanges",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "TransferAmountChanges",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "After",
                table: "TransferCorrections",
                type: "nvarchar(2454)",
                maxLength: 2454,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Before",
                table: "TransferCorrections",
                type: "nvarchar(2454)",
                maxLength: 2454,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "TransferCorrections",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "TransferCorrections",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "TransferStatusPeriods",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "TransferStatusPeriods",
                type: "nvarchar(854)",
                maxLength: 854,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "People",
                type: "nvarchar(44)",
                maxLength: 44,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "Accounts",
                type: "nvarchar(44)",
                maxLength: 44,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "OutgoingCategories",
                type: "nvarchar(44)",
                maxLength: 44,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_HouseholdId_NameKey",
                table: "People",
                columns: new[] { "HouseholdId", "NameKey" },
                unique: true,
                filter: "[NameKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_HouseholdId_ParentAccountId_NameKey",
                table: "Accounts",
                columns: new[] { "HouseholdId", "ParentAccountId", "NameKey" },
                unique: true,
                filter: "[ParentAccountId] IS NOT NULL AND [NameKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingCategories_HouseholdId_NameKey",
                table: "OutgoingCategories",
                columns: new[] { "HouseholdId", "NameKey" },
                unique: true,
                filter: "[NameKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only possible before the app has encrypted anything: encrypted values don't fit the old columns.

            migrationBuilder.DropIndex(
                name: "IX_People_HouseholdId_NameKey",
                table: "People");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_HouseholdId_ParentAccountId_NameKey",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingCategories_HouseholdId_NameKey",
                table: "OutgoingCategories");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "OutgoingCategories");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Accounts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(454)",
                oldMaxLength: 454,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "Accounts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                table: "Accounts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(454)",
                oldMaxLength: 454,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ActorName",
                table: "ActivityEntries",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "Summary",
                table: "ActivityEntries",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1254)",
                oldMaxLength: 1254,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Households",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(454)",
                oldMaxLength: 454,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "Households",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "HouseholdMembers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "Invitations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "IncomeAmountChanges",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "IncomeAmountChanges",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "After",
                table: "IncomeCorrections",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2454)",
                oldMaxLength: 2454,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Before",
                table: "IncomeCorrections",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2454)",
                oldMaxLength: 2454,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "IncomeCorrections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "IncomeCorrections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "IncomeStatusPeriods",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "IncomeStatusPeriods",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "IncomeStreams",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(454)",
                oldMaxLength: 454,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "IncomeStreams",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContactPhone",
                table: "Outgoings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(174)",
                oldMaxLength: 174,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Outgoings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(454)",
                oldMaxLength: 454,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "Outgoings",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "Outgoings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Recipient",
                table: "Outgoings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(454)",
                oldMaxLength: 454,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceLabel",
                table: "Outgoings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(254)",
                oldMaxLength: 254,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceValue",
                table: "Outgoings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(454)",
                oldMaxLength: 454,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Website",
                table: "Outgoings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1254)",
                oldMaxLength: 1254,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "OutgoingAmountChanges",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "OutgoingAmountChanges",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "OutgoingCategories",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(254)",
                oldMaxLength: 254,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "OutgoingCategories",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "After",
                table: "OutgoingCorrections",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2454)",
                oldMaxLength: 2454,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Before",
                table: "OutgoingCorrections",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2454)",
                oldMaxLength: 2454,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "OutgoingCorrections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "OutgoingCorrections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "OutgoingStatusPeriods",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "OutgoingStatusPeriods",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "People",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(454)",
                oldMaxLength: 454,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "People",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Transfers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(454)",
                oldMaxLength: 454,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "Transfers",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "Transfers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "TransferAmountChanges",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "TransferAmountChanges",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "After",
                table: "TransferCorrections",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2454)",
                oldMaxLength: 2454,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Before",
                table: "TransferCorrections",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2454)",
                oldMaxLength: 2454,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "TransferCorrections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "TransferCorrections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "TransferStatusPeriods",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "OriginallyRecordedBy",
                table: "TransferStatusPeriods",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(854)",
                oldMaxLength: 854,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_HouseholdId_DisplayName",
                table: "People",
                columns: new[] { "HouseholdId", "DisplayName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_HouseholdId_ParentAccountId_Name",
                table: "Accounts",
                columns: new[] { "HouseholdId", "ParentAccountId", "Name" },
                unique: true,
                filter: "[ParentAccountId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingCategories_HouseholdId_Name",
                table: "OutgoingCategories",
                columns: new[] { "HouseholdId", "Name" },
                unique: true);
        }
    }
}
