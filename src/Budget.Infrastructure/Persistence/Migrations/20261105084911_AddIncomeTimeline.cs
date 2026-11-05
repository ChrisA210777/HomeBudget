using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomeTimeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Income is redesigned (BUD-30): the old gross-pay rows have no person and no monthly net
            // history, so they can't be carried over. They were test data only; people re-enter income.
            migrationBuilder.Sql("DELETE FROM IncomeStreams;");

            migrationBuilder.DropForeignKey(
                name: "FK_IncomeStreams_AspNetUsers_MemberUserId",
                table: "IncomeStreams");

            migrationBuilder.DropIndex(
                name: "IX_IncomeStreams_MemberUserId",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "AmountBasis",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "AverageGross",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "EmployerName",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "GrossAmount",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "HighestGross",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "LowestGross",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "MemberUserId",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "PayFrequency",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "IncomeStreams");

            migrationBuilder.AddColumn<Guid>(
                name: "PersonId",
                table: "IncomeStreams",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "IncomeAmountChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncomeStreamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    MonthlyNetAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomeAmountChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncomeAmountChanges_IncomeStreams_IncomeStreamId",
                        column: x => x.IncomeStreamId,
                        principalTable: "IncomeStreams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncomeCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncomeStreamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Before = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    After = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomeCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncomeCorrections_IncomeStreams_IncomeStreamId",
                        column: x => x.IncomeStreamId,
                        principalTable: "IncomeStreams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncomeStatusPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncomeStreamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReducedMonthlyAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomeStatusPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncomeStatusPeriods_IncomeStreams_IncomeStreamId",
                        column: x => x.IncomeStreamId,
                        principalTable: "IncomeStreams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncomeStreams_PersonId",
                table: "IncomeStreams",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_IncomeAmountChanges_IncomeStreamId_EffectiveFrom",
                table: "IncomeAmountChanges",
                columns: new[] { "IncomeStreamId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomeCorrections_IncomeStreamId",
                table: "IncomeCorrections",
                column: "IncomeStreamId");

            migrationBuilder.CreateIndex(
                name: "IX_IncomeStatusPeriods_IncomeStreamId",
                table: "IncomeStatusPeriods",
                column: "IncomeStreamId");

            migrationBuilder.AddForeignKey(
                name: "FK_IncomeStreams_People_PersonId",
                table: "IncomeStreams",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IncomeStreams_People_PersonId",
                table: "IncomeStreams");

            migrationBuilder.DropTable(
                name: "IncomeAmountChanges");

            migrationBuilder.DropTable(
                name: "IncomeCorrections");

            migrationBuilder.DropTable(
                name: "IncomeStatusPeriods");

            migrationBuilder.DropIndex(
                name: "IX_IncomeStreams_PersonId",
                table: "IncomeStreams");

            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "IncomeStreams");

            migrationBuilder.AddColumn<string>(
                name: "AmountBasis",
                table: "IncomeStreams",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AverageGross",
                table: "IncomeStreams",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployerName",
                table: "IncomeStreams",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossAmount",
                table: "IncomeStreams",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HighestGross",
                table: "IncomeStreams",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "IncomeStreams",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "IncomeStreams",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LowestGross",
                table: "IncomeStreams",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MemberUserId",
                table: "IncomeStreams",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayFrequency",
                table: "IncomeStreams",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "IncomeStreams",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_IncomeStreams_MemberUserId",
                table: "IncomeStreams",
                column: "MemberUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_IncomeStreams_AspNetUsers_MemberUserId",
                table: "IncomeStreams",
                column: "MemberUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}
