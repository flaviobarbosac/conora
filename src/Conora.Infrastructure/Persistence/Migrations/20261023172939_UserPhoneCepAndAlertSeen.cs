using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserPhoneCepAndAlertSeen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cep",
                table: "users",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cep",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "users");
        }
    }
}
