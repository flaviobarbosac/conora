using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameChartAccountsToCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Rename(migrationBuilder, "chart_accounts", "categories", "ChartAccount", "Category");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Rename(migrationBuilder, "categories", "chart_accounts", "Category", "ChartAccount");
        }

        private static void Rename(MigrationBuilder mb, string fromTable, string toTable, string fromName, string toName)
        {
            mb.DropPrimaryKey(name: "PK_" + fromTable, table: fromTable);
            mb.RenameTable(name: fromTable, newName: toTable);
            mb.AddPrimaryKey(name: "PK_" + toTable, table: toTable, column: "Id");

            foreach (var suffix in new[] { "UsuarioId_Code", "UsuarioId_ParentId", "UsuarioId_Section" })
            {
                mb.RenameIndex(name: $"IX_{fromTable}_{suffix}", table: toTable, newName: $"IX_{toTable}_{suffix}");
            }

            foreach (var table in new[] { "patrimony_items", "life_projects", "entries", "card_purchases", "budget_lines" })
            {
                mb.RenameColumn(name: fromName + "Id", table: table, newName: toName + "Id");
            }

            mb.RenameIndex(name: $"IX_patrimony_items_UsuarioId_{fromName}", table: "patrimony_items", newName: $"IX_patrimony_items_UsuarioId_{toName}");
            mb.RenameIndex(name: $"IX_life_projects_UsuarioId_{fromName}Id", table: "life_projects", newName: $"IX_life_projects_UsuarioId_{toName}Id");
            mb.RenameIndex(name: $"IX_entries_UsuarioId_{fromName}Id", table: "entries", newName: $"IX_entries_UsuarioId_{toName}Id");
            mb.RenameIndex(name: $"IX_budget_lines_Budget_{fromName}", table: "budget_lines", newName: $"IX_budget_lines_Budget_{toName}");
        }
    }
}