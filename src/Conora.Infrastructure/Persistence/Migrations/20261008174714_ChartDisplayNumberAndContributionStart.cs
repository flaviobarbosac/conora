using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChartDisplayNumberAndContributionStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_life_projects_ChartAccountId",
                table: "life_projects");

            migrationBuilder.Sql("""
                UPDATE life_projects
                SET "DueDate" = COALESCE("DueDate", "CreatedAt")
                WHERE "DueDate" IS NULL;
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "DueDate",
                table: "life_projects",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContributionStartYm",
                table: "life_projects",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE life_projects
                SET "ContributionStartYm" = to_char("DueDate" AT TIME ZONE 'UTC', 'YYYY-MM')
                WHERE "ContributionStartYm" IS NULL OR "ContributionStartYm" = '';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ContributionStartYm",
                table: "life_projects",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayNumber",
                table: "chart_accounts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_life_projects_UsuarioId_ChartAccountId",
                table: "life_projects",
                columns: new[] { "UsuarioId", "ChartAccountId" },
                unique: true,
                filter: "\"ChartAccountId\" IS NOT NULL AND \"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_life_projects_UsuarioId_ChartAccountId",
                table: "life_projects");

            migrationBuilder.DropColumn(
                name: "ContributionStartYm",
                table: "life_projects");

            migrationBuilder.DropColumn(
                name: "DisplayNumber",
                table: "chart_accounts");

            migrationBuilder.AlterColumn<DateTime>(
                name: "DueDate",
                table: "life_projects",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.CreateIndex(
                name: "IX_life_projects_ChartAccountId",
                table: "life_projects",
                column: "ChartAccountId");
        }
    }
}
