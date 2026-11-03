using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261103160000_UserJornadaProfile")]
    /// <inheritdoc />
    public partial class UserJornadaProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomeJourneyDisplay",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "shown");

            migrationBuilder.AddColumn<bool>(
                name: "JornadaDivida",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "JornadaPatrimonio",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "JornadaProjetoVida",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "JornadaRendaFixa",
                table: "users",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "HomeJourneyDisplay", table: "users");
            migrationBuilder.DropColumn(name: "JornadaDivida", table: "users");
            migrationBuilder.DropColumn(name: "JornadaPatrimonio", table: "users");
            migrationBuilder.DropColumn(name: "JornadaProjetoVida", table: "users");
            migrationBuilder.DropColumn(name: "JornadaRendaFixa", table: "users");
        }
    }
}
