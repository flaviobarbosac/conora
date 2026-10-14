using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FamilyGroupBudgetOwnerAndSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BudgetOwnerUsuarioId",
                table: "family_groups",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("""
                UPDATE family_groups
                SET "BudgetOwnerUsuarioId" = "CreatedByUsuarioId"
                WHERE "BudgetOwnerUsuarioId" = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.AddColumn<string>(
                name: "SharingScope",
                table: "categories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Personal");

            migrationBuilder.CreateTable(
                name: "family_owner_transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromUsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToUsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_family_owner_transfers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_family_groups_BudgetOwnerUsuarioId",
                table: "family_groups",
                column: "BudgetOwnerUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_family_owner_transfers_FamilyGroupId_AcceptedAt_CancelledAt",
                table: "family_owner_transfers",
                columns: new[] { "FamilyGroupId", "AcceptedAt", "CancelledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_family_owner_transfers_Token",
                table: "family_owner_transfers",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "family_owner_transfers");

            migrationBuilder.DropIndex(
                name: "IX_family_groups_BudgetOwnerUsuarioId",
                table: "family_groups");

            migrationBuilder.DropColumn(
                name: "BudgetOwnerUsuarioId",
                table: "family_groups");

            migrationBuilder.DropColumn(
                name: "SharingScope",
                table: "categories");
        }
    }
}
