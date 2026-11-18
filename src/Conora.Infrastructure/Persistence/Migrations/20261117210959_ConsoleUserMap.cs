using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConsoleUserMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "users",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "users",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "console_map_config",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PinLabelField = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PinLabelText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShowLabel = table.Column<bool>(type: "boolean", nullable: false),
                    VisibleFieldsJson = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_console_map_config", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "console_map_config");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "users");
        }
    }
}
