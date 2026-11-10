using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Budget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FirstDayOfWeek",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IncomePeriod",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OutgoingsGrouping",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OutgoingsPeriod",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstDayOfWeek",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IncomePeriod",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OutgoingsGrouping",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OutgoingsPeriod",
                table: "AspNetUsers");
        }
    }
}
