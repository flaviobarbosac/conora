using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FamilyIntegrityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE family_groups ALTER COLUMN "BudgetOwnerUsuarioId" DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_family_owner_transfers_FromUsuarioId",
                table: "family_owner_transfers",
                column: "FromUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_family_owner_transfers_one_open",
                table: "family_owner_transfers",
                column: "FamilyGroupId",
                unique: true,
                filter: "\"AcceptedAt\" IS NULL AND \"CancelledAt\" IS NULL AND \"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_family_owner_transfers_ToUsuarioId",
                table: "family_owner_transfers",
                column: "ToUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_family_invites_FamilyGroupId",
                table: "family_invites",
                column: "FamilyGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_family_groups_CreatedByUsuarioId",
                table: "family_groups",
                column: "CreatedByUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_categories_UsuarioId_SharingScope",
                table: "categories",
                columns: new[] { "UsuarioId", "SharingScope" });

            migrationBuilder.AddForeignKey(
                name: "FK_family_group_members_family_groups_FamilyGroupId",
                table: "family_group_members",
                column: "FamilyGroupId",
                principalTable: "family_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_family_group_members_users_UsuarioId",
                table: "family_group_members",
                column: "UsuarioId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_family_groups_users_BudgetOwnerUsuarioId",
                table: "family_groups",
                column: "BudgetOwnerUsuarioId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_family_groups_users_CreatedByUsuarioId",
                table: "family_groups",
                column: "CreatedByUsuarioId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_family_invites_family_groups_FamilyGroupId",
                table: "family_invites",
                column: "FamilyGroupId",
                principalTable: "family_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_family_invites_users_InviterUsuarioId",
                table: "family_invites",
                column: "InviterUsuarioId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_family_notices_users_UsuarioId",
                table: "family_notices",
                column: "UsuarioId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_family_owner_transfers_family_groups_FamilyGroupId",
                table: "family_owner_transfers",
                column: "FamilyGroupId",
                principalTable: "family_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_family_owner_transfers_users_FromUsuarioId",
                table: "family_owner_transfers",
                column: "FromUsuarioId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_family_owner_transfers_users_ToUsuarioId",
                table: "family_owner_transfers",
                column: "ToUsuarioId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_family_group_members_family_groups_FamilyGroupId",
                table: "family_group_members");

            migrationBuilder.DropForeignKey(
                name: "FK_family_group_members_users_UsuarioId",
                table: "family_group_members");

            migrationBuilder.DropForeignKey(
                name: "FK_family_groups_users_BudgetOwnerUsuarioId",
                table: "family_groups");

            migrationBuilder.DropForeignKey(
                name: "FK_family_groups_users_CreatedByUsuarioId",
                table: "family_groups");

            migrationBuilder.DropForeignKey(
                name: "FK_family_invites_family_groups_FamilyGroupId",
                table: "family_invites");

            migrationBuilder.DropForeignKey(
                name: "FK_family_invites_users_InviterUsuarioId",
                table: "family_invites");

            migrationBuilder.DropForeignKey(
                name: "FK_family_notices_users_UsuarioId",
                table: "family_notices");

            migrationBuilder.DropForeignKey(
                name: "FK_family_owner_transfers_family_groups_FamilyGroupId",
                table: "family_owner_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_family_owner_transfers_users_FromUsuarioId",
                table: "family_owner_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_family_owner_transfers_users_ToUsuarioId",
                table: "family_owner_transfers");

            migrationBuilder.DropIndex(
                name: "IX_family_owner_transfers_FromUsuarioId",
                table: "family_owner_transfers");

            migrationBuilder.DropIndex(
                name: "IX_family_owner_transfers_one_open",
                table: "family_owner_transfers");

            migrationBuilder.DropIndex(
                name: "IX_family_owner_transfers_ToUsuarioId",
                table: "family_owner_transfers");

            migrationBuilder.DropIndex(
                name: "IX_family_invites_FamilyGroupId",
                table: "family_invites");

            migrationBuilder.DropIndex(
                name: "IX_family_groups_CreatedByUsuarioId",
                table: "family_groups");

            migrationBuilder.DropIndex(
                name: "IX_categories_UsuarioId_SharingScope",
                table: "categories");
        }
    }
}
