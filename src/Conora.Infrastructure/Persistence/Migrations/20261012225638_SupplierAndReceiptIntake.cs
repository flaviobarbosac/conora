using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Conora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierAndReceiptIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receipt_intakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ImageObjectKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExtractedCnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    ExtractedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExtractedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ExtractedOccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProposedCategoryCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SelectedBankAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    WhatsAppDraftId = table.Column<Guid>(type: "uuid", nullable: true),
                    PhoneE164 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    WhatsAppAwaiting = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ImageExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_intakes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefaultCategoryCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    NeedsReview = table.Column<bool>(type: "boolean", nullable: false),
                    ClassifiedByUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClassifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_intakes_ImageExpiresAt",
                table: "receipt_intakes",
                column: "ImageExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_intakes_PhoneE164_WhatsAppAwaiting",
                table: "receipt_intakes",
                columns: new[] { "PhoneE164", "WhatsAppAwaiting" });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_intakes_UsuarioId_Status",
                table: "receipt_intakes",
                columns: new[] { "UsuarioId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_Cnpj",
                table: "suppliers",
                column: "Cnpj",
                unique: true,
                filter: "\"Cnpj\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_NormalizedName",
                table: "suppliers",
                column: "NormalizedName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_intakes");

            migrationBuilder.DropTable(
                name: "suppliers");
        }
    }
}
