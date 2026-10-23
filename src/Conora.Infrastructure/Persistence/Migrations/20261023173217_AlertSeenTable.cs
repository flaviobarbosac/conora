using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlertSeenTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_seen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetenceYm = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_seen", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_seen_Usuario_Ym_Code_Category",
                table: "alert_seen",
                columns: new[] { "UsuarioId", "CompetenceYm", "Code", "CategoryId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_seen");
        }
    }
}
