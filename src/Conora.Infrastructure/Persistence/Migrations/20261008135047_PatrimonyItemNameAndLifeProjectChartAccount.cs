using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PatrimonyItemNameAndLifeProjectChartAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_patrimony_items_UsuarioId_ChartAccount",
                table: "patrimony_items");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "patrimony_items",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE patrimony_items AS p
                SET "Name" = COALESCE(NULLIF(TRIM(c."Name"), ''), 'Item')
                FROM chart_accounts AS c
                WHERE c."Id" = p."ChartAccountId";

                UPDATE patrimony_items
                SET "Name" = 'Item'
                WHERE "Name" IS NULL OR TRIM("Name") = '';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "patrimony_items",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ChartAccountId",
                table: "life_projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_patrimony_items_UsuarioId_ChartAccount",
                table: "patrimony_items",
                columns: new[] { "UsuarioId", "ChartAccountId" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_life_projects_ChartAccountId",
                table: "life_projects",
                column: "ChartAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_patrimony_items_UsuarioId_ChartAccount",
                table: "patrimony_items");

            migrationBuilder.DropIndex(
                name: "IX_life_projects_ChartAccountId",
                table: "life_projects");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "patrimony_items");

            migrationBuilder.DropColumn(
                name: "ChartAccountId",
                table: "life_projects");

            migrationBuilder.CreateIndex(
                name: "IX_patrimony_items_UsuarioId_ChartAccount",
                table: "patrimony_items",
                columns: new[] { "UsuarioId", "ChartAccountId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }
    }
}
