using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutgoings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutgoingCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutgoingCategories_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Outgoings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FirstPaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RenewalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RenewalEveryMonths = table.Column<int>(type: "int", nullable: false),
                    AutoRenews = table.Column<bool>(type: "bit", nullable: false),
                    AlertPeriod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaidFrom = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReferenceLabel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReferenceValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outgoings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Outgoings_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Outgoings_OutgoingCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "OutgoingCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Outgoings_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutgoingAmountChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutgoingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Every = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingAmountChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutgoingAmountChanges_Outgoings_OutgoingId",
                        column: x => x.OutgoingId,
                        principalTable: "Outgoings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutgoingCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutgoingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_OutgoingCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutgoingCorrections_Outgoings_OutgoingId",
                        column: x => x.OutgoingId,
                        principalTable: "Outgoings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutgoingStatusPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutgoingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ResumesOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutgoingStatusPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutgoingStatusPeriods_Outgoings_OutgoingId",
                        column: x => x.OutgoingId,
                        principalTable: "Outgoings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingAmountChanges_OutgoingId_EffectiveFrom",
                table: "OutgoingAmountChanges",
                columns: new[] { "OutgoingId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingCategories_HouseholdId_Name",
                table: "OutgoingCategories",
                columns: new[] { "HouseholdId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingCorrections_OutgoingId",
                table: "OutgoingCorrections",
                column: "OutgoingId");

            migrationBuilder.CreateIndex(
                name: "IX_Outgoings_CategoryId",
                table: "Outgoings",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Outgoings_HouseholdId",
                table: "Outgoings",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_Outgoings_PersonId",
                table: "Outgoings",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingStatusPeriods_OutgoingId",
                table: "OutgoingStatusPeriods",
                column: "OutgoingId");

            // Households created before this migration get the default categories that new
            // households get from OutgoingCategory.CreateDefaults (BUD-47). Keep the two in step.
            migrationBuilder.Sql("""
                INSERT INTO OutgoingCategories (Id, HouseholdId, Name, SortOrder, IsHidden, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
                SELECT NEWID(), h.Id, d.Name, d.SortOrder, 0, SYSDATETIMEOFFSET(), NULL, SYSDATETIMEOFFSET(), NULL
                FROM Households h
                CROSS JOIN (VALUES
                    (N'Housing', 10),
                    (N'Utilities', 20),
                    (N'Phone & internet', 30),
                    (N'Insurance', 40),
                    (N'Home maintenance', 50),
                    (N'Car & travel', 60),
                    (N'Pets', 70),
                    (N'Health & personal care', 80),
                    (N'Food & household', 90),
                    (N'Children', 100),
                    (N'Subscriptions & TV', 110),
                    (N'Leisure & memberships', 120),
                    (N'Gifts & giving', 130),
                    (N'Banking & fees', 140),
                    (N'Savings & set-asides', 150),
                    (N'Discretionary spending', 160)
                ) AS d(Name, SortOrder);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutgoingAmountChanges");

            migrationBuilder.DropTable(
                name: "OutgoingCorrections");

            migrationBuilder.DropTable(
                name: "OutgoingStatusPeriods");

            migrationBuilder.DropTable(
                name: "Outgoings");

            migrationBuilder.DropTable(
                name: "OutgoingCategories");
        }
    }
}
