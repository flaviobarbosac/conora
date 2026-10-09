using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LifeProjectAllowMultiplePerChartAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_life_projects_UsuarioId_ChartAccountId",
                table: "life_projects");

            migrationBuilder.CreateIndex(
                name: "IX_life_projects_UsuarioId_ChartAccountId",
                table: "life_projects",
                columns: new[] { "UsuarioId", "ChartAccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_life_projects_UsuarioId_ChartAccountId",
                table: "life_projects");

            migrationBuilder.CreateIndex(
                name: "IX_life_projects_UsuarioId_ChartAccountId",
                table: "life_projects",
                columns: new[] { "UsuarioId", "ChartAccountId" },
                unique: true,
                filter: "\"ChartAccountId\" IS NOT NULL AND \"DeletedAt\" IS NULL");
        }
    }
}
