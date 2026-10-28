using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_feedbacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioNome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Modulo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Rota = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Nota = table.Column<int>(type: "integer", nullable: false),
                    Comentario = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LidoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    LidoPorUsuarioNome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Resposta = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RespondidoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    RespondidoPorUsuarioNome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RespondidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespostaLidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_feedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_feedbacks_users_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_feedbacks_Status",
                table: "user_feedbacks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_user_feedbacks_UsuarioId_CreatedAt",
                table: "user_feedbacks",
                columns: new[] { "UsuarioId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_feedbacks");
        }
    }
}
