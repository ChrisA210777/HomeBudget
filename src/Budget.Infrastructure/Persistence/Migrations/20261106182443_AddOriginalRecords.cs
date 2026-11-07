using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOriginalRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginallyRecordedAt",
                table: "People",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginallyRecordedBy",
                table: "People",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginallyRecordedAt",
                table: "OutgoingStatusPeriods",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginallyRecordedBy",
                table: "OutgoingStatusPeriods",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginallyRecordedAt",
                table: "Outgoings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginallyRecordedBy",
                table: "Outgoings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginallyRecordedAt",
                table: "OutgoingCorrections",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginallyRecordedBy",
                table: "OutgoingCorrections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginallyRecordedAt",
                table: "OutgoingCategories",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginallyRecordedBy",
                table: "OutgoingCategories",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginallyRecordedAt",
                table: "OutgoingAmountChanges",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginallyRecordedBy",
                table: "OutgoingAmountChanges",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginallyRecordedAt",
                table: "IncomeStreams",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginallyRecordedBy",
                table: "IncomeStreams",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginallyRecordedAt",
                table: "IncomeStatusPeriods",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginallyRecordedBy",
                table: "IncomeStatusPeriods",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginallyRecordedAt",
                table: "IncomeCorrections",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginallyRecordedBy",
                table: "IncomeCorrections",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginallyRecordedAt",
                table: "IncomeAmountChanges",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginallyRecordedBy",
                table: "IncomeAmountChanges",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginallyRecordedAt",
                table: "Households",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginallyRecordedBy",
                table: "Households",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginallyRecordedAt",
                table: "HouseholdMembers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginallyRecordedBy",
                table: "HouseholdMembers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginallyRecordedAt",
                table: "People");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedBy",
                table: "People");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedAt",
                table: "OutgoingStatusPeriods");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedBy",
                table: "OutgoingStatusPeriods");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedAt",
                table: "Outgoings");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedBy",
                table: "Outgoings");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedAt",
                table: "OutgoingCorrections");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedBy",
                table: "OutgoingCorrections");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedAt",
                table: "OutgoingCategories");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedBy",
                table: "OutgoingCategories");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedAt",
                table: "OutgoingAmountChanges");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedBy",
                table: "OutgoingAmountChanges");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedAt",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedBy",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedAt",
                table: "IncomeStatusPeriods");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedBy",
                table: "IncomeStatusPeriods");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedAt",
                table: "IncomeCorrections");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedBy",
                table: "IncomeCorrections");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedAt",
                table: "IncomeAmountChanges");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedBy",
                table: "IncomeAmountChanges");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedAt",
                table: "Households");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedBy",
                table: "Households");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedAt",
                table: "HouseholdMembers");

            migrationBuilder.DropColumn(
                name: "OriginallyRecordedBy",
                table: "HouseholdMembers");
        }
    }
}
