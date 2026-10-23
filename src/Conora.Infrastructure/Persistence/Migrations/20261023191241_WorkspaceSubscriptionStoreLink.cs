using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceSubscriptionStoreLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Store",
                table: "workspace_subscriptions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreProductId",
                table: "workspace_subscriptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoreSubscriptionId",
                table: "workspace_subscriptions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_subscriptions_StoreSubscriptionId",
                table: "workspace_subscriptions",
                columns: new[] { "Store", "StoreSubscriptionId" },
                unique: true,
                filter: "\"StoreSubscriptionId\" IS NOT NULL AND \"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_workspace_subscriptions_StoreSubscriptionId",
                table: "workspace_subscriptions");

            migrationBuilder.DropColumn(
                name: "Store",
                table: "workspace_subscriptions");

            migrationBuilder.DropColumn(
                name: "StoreProductId",
                table: "workspace_subscriptions");

            migrationBuilder.DropColumn(
                name: "StoreSubscriptionId",
                table: "workspace_subscriptions");
        }
    }
}
