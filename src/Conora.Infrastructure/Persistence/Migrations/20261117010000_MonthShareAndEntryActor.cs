using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261117010000_MonthShareAndEntryActor")]
    public partial class MonthShareAndEntryActor : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActorUsuarioId",
                table: "entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "month_shares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientUsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultantUsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetenceYm = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_month_shares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_month_shares_users_ClientUsuarioId",
                        column: x => x.ClientUsuarioId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_month_shares_users_ConsultantUsuarioId",
                        column: x => x.ConsultantUsuarioId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_month_shares_client_consultant_month",
                table: "month_shares",
                columns: new[] { "ClientUsuarioId", "ConsultantUsuarioId", "CompetenceYm" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "month_shares");
            migrationBuilder.DropColumn(name: "ActorUsuarioId", table: "entries");
        }
    }
}
