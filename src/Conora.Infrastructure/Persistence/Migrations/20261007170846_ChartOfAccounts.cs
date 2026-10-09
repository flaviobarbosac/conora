using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChartOfAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropIndex(
                name: "IX_patrimony_items_UsuarioId_Kind",
                table: "patrimony_items");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "patrimony_items");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "patrimony_items");

            migrationBuilder.RenameColumn(
                name: "CategoryId",
                table: "entries",
                newName: "ChartAccountId");

            migrationBuilder.RenameIndex(
                name: "IX_entries_UsuarioId_CategoryId",
                table: "entries",
                newName: "IX_entries_UsuarioId_ChartAccountId");

            migrationBuilder.RenameColumn(
                name: "CategoryId",
                table: "card_purchases",
                newName: "ChartAccountId");

            migrationBuilder.RenameColumn(
                name: "CategoryId",
                table: "budget_lines",
                newName: "ChartAccountId");

            migrationBuilder.RenameIndex(
                name: "IX_budget_lines_Budget_Category",
                table: "budget_lines",
                newName: "IX_budget_lines_Budget_ChartAccount");

            migrationBuilder.AddColumn<Guid>(
                name: "ChartAccountId",
                table: "patrimony_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "chart_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Section = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chart_accounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_patrimony_items_UsuarioId_ChartAccount",
                table: "patrimony_items",
                columns: new[] { "UsuarioId", "ChartAccountId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_chart_accounts_UsuarioId_Code",
                table: "chart_accounts",
                columns: new[] { "UsuarioId", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL AND \"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_chart_accounts_UsuarioId_ParentId",
                table: "chart_accounts",
                columns: new[] { "UsuarioId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_chart_accounts_UsuarioId_Section",
                table: "chart_accounts",
                columns: new[] { "UsuarioId", "Section" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chart_accounts");

            migrationBuilder.DropIndex(
                name: "IX_patrimony_items_UsuarioId_ChartAccount",
                table: "patrimony_items");

            migrationBuilder.DropColumn(
                name: "ChartAccountId",
                table: "patrimony_items");

            migrationBuilder.RenameColumn(
                name: "ChartAccountId",
                table: "entries",
                newName: "CategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_entries_UsuarioId_ChartAccountId",
                table: "entries",
                newName: "IX_entries_UsuarioId_CategoryId");

            migrationBuilder.RenameColumn(
                name: "ChartAccountId",
                table: "card_purchases",
                newName: "CategoryId");

            migrationBuilder.RenameColumn(
                name: "ChartAccountId",
                table: "budget_lines",
                newName: "CategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_budget_lines_Budget_ChartAccount",
                table: "budget_lines",
                newName: "IX_budget_lines_Budget_Category");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "patrimony_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "patrimony_items",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetBlock = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GroupName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsEssential = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_patrimony_items_UsuarioId_Kind",
                table: "patrimony_items",
                columns: new[] { "UsuarioId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_categories_UsuarioId_Code",
                table: "categories",
                columns: new[] { "UsuarioId", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL AND \"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_categories_UsuarioId_Kind",
                table: "categories",
                columns: new[] { "UsuarioId", "Kind" });
        }
    }
}
