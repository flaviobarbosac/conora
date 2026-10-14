using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261014194500_CategoryFamilyReplica")]
    public partial class CategoryFamilyReplica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUsuarioId",
                table: "categories",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyKey",
                table: "categories",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE categories
                SET "CreatedByUsuarioId" = "UsuarioId",
                    "FamilyKey" = "Id"
                WHERE "IsSystem" = false
                  AND "Level" = 'Analytical'
                  AND "DeletedAt" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_categories_FamilyKey",
                table: "categories",
                column: "FamilyKey",
                filter: "\"FamilyKey\" IS NOT NULL AND \"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_categories_FamilyKey",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "FamilyKey",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "CreatedByUsuarioId",
                table: "categories");
        }
    }
}
