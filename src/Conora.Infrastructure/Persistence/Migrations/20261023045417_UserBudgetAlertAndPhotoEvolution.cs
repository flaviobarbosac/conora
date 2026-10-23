using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserBudgetAlertAndPhotoEvolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BudgetAlertPercent",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 70);

            migrationBuilder.AddColumn<bool>(
                name: "PhotoEvolutionEnabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BudgetAlertPercent",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PhotoEvolutionEnabled",
                table: "users");
        }
    }
}
