using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierUserCategoryAndReceiptPurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerificationExpiresAtUtc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailVerificationTokenHash",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "receipt_intakes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Expense");

            migrationBuilder.CreateTable(
                name: "user_milestones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Age = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_milestones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmailVerificationTokenHash",
                table: "users",
                column: "EmailVerificationTokenHash",
                unique: true,
                filter: "\"EmailVerificationTokenHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_user_milestones_Usuario_Key",
                table: "user_milestones",
                columns: new[] { "UsuarioId", "Key" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_milestones");

            migrationBuilder.DropIndex(
                name: "IX_Users_EmailVerificationTokenHash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationExpiresAtUtc",
                table: "users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationTokenHash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "receipt_intakes");
        }
    }
}
