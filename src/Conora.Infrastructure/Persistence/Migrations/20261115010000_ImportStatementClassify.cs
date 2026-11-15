using Conora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261115010000_ImportStatementClassify")]
    public partial class ImportStatementClassify : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompetenceYm",
                table: "import_batches",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "import_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedBankCode",
                table: "import_batches",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedAgency",
                table: "import_batches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedAccountNumber",
                table: "import_batches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedCheckDigit",
                table: "import_batches",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_import_batches_UsuarioId_CompetenceYm_Status",
                table: "import_batches",
                columns: new[] { "UsuarioId", "CompetenceYm", "Status" });

            migrationBuilder.AddColumn<string>(
                name: "RowStatus",
                table: "import_preview_rows",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<Guid>(
                name: "SuggestedCategoryId",
                table: "import_preview_rows",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EntryId",
                table: "import_preview_rows",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortIndex",
                table: "import_preview_rows",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE import_preview_rows
                SET "RowStatus" = CASE WHEN "WillImport" THEN 'Pending' ELSE 'Ignored' END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_import_preview_rows_BatchId_RowStatus_SortIndex",
                table: "import_preview_rows",
                columns: new[] { "BatchId", "RowStatus", "SortIndex" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_import_preview_rows_BatchId_RowStatus_SortIndex",
                table: "import_preview_rows");

            migrationBuilder.DropColumn(name: "RowStatus", table: "import_preview_rows");
            migrationBuilder.DropColumn(name: "SuggestedCategoryId", table: "import_preview_rows");
            migrationBuilder.DropColumn(name: "EntryId", table: "import_preview_rows");
            migrationBuilder.DropColumn(name: "SortIndex", table: "import_preview_rows");

            migrationBuilder.DropIndex(
                name: "IX_import_batches_UsuarioId_CompetenceYm_Status",
                table: "import_batches");

            migrationBuilder.DropColumn(name: "CompetenceYm", table: "import_batches");
            migrationBuilder.DropColumn(name: "AccountId", table: "import_batches");
            migrationBuilder.DropColumn(name: "DetectedBankCode", table: "import_batches");
            migrationBuilder.DropColumn(name: "DetectedAgency", table: "import_batches");
            migrationBuilder.DropColumn(name: "DetectedAccountNumber", table: "import_batches");
            migrationBuilder.DropColumn(name: "DetectedCheckDigit", table: "import_batches");
        }
    }
}
