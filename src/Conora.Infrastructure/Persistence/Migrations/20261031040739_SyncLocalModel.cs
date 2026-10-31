using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncLocalModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CheckDigit",
                table: "accounts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2)",
                oldMaxLength: 3,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CheckDigit",
                table: "accounts",
                type: "character varying(2)",
                maxLength: 3,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldNullable: true);
        }
    }
}
