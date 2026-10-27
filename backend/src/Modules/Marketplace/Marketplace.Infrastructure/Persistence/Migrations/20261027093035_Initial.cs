using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "marketplace");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    applicant_person_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    registration_no = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    contact_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    whatsapp = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    verified_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    rejection_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    checklist_done = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    min_order_cents = table.Column<long>(type: "bigint", nullable: false),
                    delivery_fee_cents = table.Column<long>(type: "bigint", nullable: false),
                    payment_terms = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_areas",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    municipality_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    days = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_supplier_areas_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "marketplace",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_products",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    catalogue_product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    barcode = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    units_per_case = table.Column<int>(type: "int", nullable: false),
                    case_price_cents = table.Column<long>(type: "bigint", nullable: false),
                    vat_included = table.Column<bool>(type: "bit", nullable: false),
                    in_stock = table.Column<bool>(type: "bit", nullable: false),
                    expiry_policy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_supplier_products_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "marketplace",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "marketplace",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_areas_municipality_code",
                schema: "marketplace",
                table: "supplier_areas",
                column: "municipality_code");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_areas_supplier_id_municipality_code",
                schema: "marketplace",
                table: "supplier_areas",
                columns: new[] { "supplier_id", "municipality_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_products_catalogue_product_id",
                schema: "marketplace",
                table: "supplier_products",
                column: "catalogue_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_products_supplier_id_barcode",
                schema: "marketplace",
                table: "supplier_products",
                columns: new[] { "supplier_id", "barcode" },
                unique: true,
                filter: "barcode IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_organisation_id",
                schema: "marketplace",
                table: "suppliers",
                column: "organisation_id",
                unique: true,
                filter: "organisation_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_status",
                schema: "marketplace",
                table: "suppliers",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "supplier_areas",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "supplier_products",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "marketplace");
        }
    }
}
