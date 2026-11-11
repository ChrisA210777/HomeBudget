using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClearPaymentDetailsByKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Payment details only where they apply (BUD-105): savings keep how they're paid, allowances nothing.
            migrationBuilder.Sql("""
                UPDATE Outgoings SET ReferenceLabel = NULL, ReferenceValue = NULL, Website = NULL, ContactPhone = NULL
                WHERE Kind <> N'Bill';

                UPDATE Outgoings SET PaymentMethod = NULL
                WHERE Kind = N'Allowance';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The cleared details can't be brought back.
        }
    }
}
