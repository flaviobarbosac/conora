using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BudgetLineExpenseDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExpenseDay",
                table: "budget_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_budget_lines_Budget_ExpenseDay",
                table: "budget_lines",
                columns: new[] { "BudgetId", "ExpenseDay" },
                filter: "\"ExpenseDay\" IS NOT NULL AND \"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_budget_lines_Budget_ExpenseDay",
                table: "budget_lines");

            migrationBuilder.DropColumn(
                name: "ExpenseDay",
                table: "budget_lines");
        }
    }
}
