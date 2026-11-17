using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CheckPendingModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_month_shares_ConsultantUsuarioId",
                table: "month_shares",
                column: "ConsultantUsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_month_shares_ConsultantUsuarioId",
                table: "month_shares");
        }
    }
}
