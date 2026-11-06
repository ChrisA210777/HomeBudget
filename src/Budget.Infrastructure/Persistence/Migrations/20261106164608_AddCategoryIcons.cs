using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryIcons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "OutgoingCategories",
                type: "varchar(30)",
                unicode: false,
                maxLength: 30,
                nullable: false,
                defaultValue: "label");

            // Existing households' default categories get the same icons as a new household's (BUD-69).
            // Names compare case-insensitively under the database's collation, as category names do.
            migrationBuilder.Sql(@"
                UPDATE OutgoingCategories SET Icon = CASE Name
                    WHEN N'Housing' THEN 'home'
                    WHEN N'Utilities' THEN 'bolt'
                    WHEN N'Phone & internet' THEN 'wifi'
                    WHEN N'Insurance' THEN 'shield'
                    WHEN N'Home maintenance' THEN 'build'
                    WHEN N'Car & travel' THEN 'car'
                    WHEN N'Pets' THEN 'pets'
                    WHEN N'Health & personal care' THEN 'health'
                    WHEN N'Food & household' THEN 'cart'
                    WHEN N'Children' THEN 'child'
                    WHEN N'Subscriptions & TV' THEN 'tv'
                    WHEN N'Leisure & memberships' THEN 'ticket'
                    WHEN N'Gifts & giving' THEN 'gift'
                    WHEN N'Banking & fees' THEN 'bank'
                    WHEN N'Savings & set-asides' THEN 'savings'
                    WHEN N'Discretionary spending' THEN 'wallet'
                END
                WHERE Name IN (N'Housing', N'Utilities', N'Phone & internet', N'Insurance', N'Home maintenance', N'Car & travel', N'Pets', N'Health & personal care', N'Food & household', N'Children', N'Subscriptions & TV', N'Leisure & memberships', N'Gifts & giving', N'Banking & fees', N'Savings & set-asides', N'Discretionary spending');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Icon",
                table: "OutgoingCategories");
        }
    }
}
