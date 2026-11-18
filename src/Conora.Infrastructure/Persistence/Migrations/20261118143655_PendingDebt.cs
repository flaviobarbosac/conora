using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PendingDebt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pending_debts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetenceYm = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpenAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_debts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pending_debts_User_Month_Category",
                table: "pending_debts",
                columns: new[] { "UsuarioId", "CompetenceYm", "CategoryId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_debts");
        }
    }
}
