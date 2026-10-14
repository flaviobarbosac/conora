using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261014203200_CategoryPendingReview")]
    public partial class CategoryPendingReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPendingReview",
                table: "categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_categories_UsuarioId_IsPendingReview",
                table: "categories",
                columns: new[] { "UsuarioId", "IsPendingReview" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_categories_UsuarioId_IsPendingReview",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "IsPendingReview",
                table: "categories");
        }
    }
}
