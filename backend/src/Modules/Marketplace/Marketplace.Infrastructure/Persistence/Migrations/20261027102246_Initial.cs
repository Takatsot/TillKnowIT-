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
                name: "group_deals",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bulk_price_cents = table.Column<long>(type: "bigint", nullable: false),
                    min_total_cases = table.Column<int>(type: "int", nullable: false),
                    closes_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    area_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_deals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "marketplace_settings",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_marketplace_settings", x => x.id);
                });

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
                name: "ratings",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    on_time = table.Column<bool>(type: "bit", nullable: false),
                    complete = table.Column<bool>(type: "bit", nullable: false),
                    score = table.Column<int>(type: "int", nullable: false),
                    comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ratings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sharing_settings",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sharing_settings", x => x.id);
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
                name: "group_commitments",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    cases = table.Column<int>(type: "int", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_commitments", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_commitments_group_deal_group_deal_id",
                        column: x => x.group_deal_id,
                        principalSchema: "marketplace",
                        principalTable: "group_deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "deals",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    terms_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    area_codes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    is_sponsored = table.Column<bool>(type: "bit", nullable: false),
                    is_cancelled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deals", x => x.id);
                    table.ForeignKey(
                        name: "fk_deals_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "marketplace",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "last_prices",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    barcode = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false),
                    case_price_cents = table.Column<long>(type: "bigint", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_last_prices", x => x.id);
                    table.ForeignKey(
                        name: "fk_last_prices_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "marketplace",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    delivery_date = table.Column<DateOnly>(type: "date", nullable: true),
                    payment_method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    payment_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    min_order_cents = table.Column<long>(type: "bigint", nullable: false),
                    delivery_fee_cents = table.Column<long>(type: "bigint", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_orders_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "marketplace",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shop_suppliers",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    last_order_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_suppliers", x => x.id);
                    table.ForeignKey(
                        name: "fk_shop_suppliers_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "marketplace",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    barcode = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
                    units_per_case = table.Column<int>(type: "int", nullable: false),
                    ordered_cases = table.Column<int>(type: "int", nullable: false),
                    case_price_cents = table.Column<long>(type: "bigint", nullable: false),
                    vat_included = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    confirmed_cases = table.Column<int>(type: "int", nullable: true),
                    delivered_cases = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "marketplace",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deals_is_cancelled_ends_at",
                schema: "marketplace",
                table: "deals",
                columns: new[] { "is_cancelled", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_deals_supplier_id_starts_at",
                schema: "marketplace",
                table: "deals",
                columns: new[] { "supplier_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_group_commitments_group_deal_id_shop_id",
                schema: "marketplace",
                table: "group_commitments",
                columns: new[] { "group_deal_id", "shop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_deals_area_code_status",
                schema: "marketplace",
                table: "group_deals",
                columns: new[] { "area_code", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_group_deals_supplier_id_status",
                schema: "marketplace",
                table: "group_deals",
                columns: new[] { "supplier_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_last_prices_shop_id_supplier_id_barcode",
                schema: "marketplace",
                table: "last_prices",
                columns: new[] { "shop_id", "supplier_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_last_prices_supplier_id",
                schema: "marketplace",
                table: "last_prices",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_marketplace_settings_key",
                schema: "marketplace",
                table: "marketplace_settings",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "marketplace",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_order_id",
                schema: "marketplace",
                table: "purchase_order_lines",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_shop_id_created_at",
                schema: "marketplace",
                table: "purchase_orders",
                columns: new[] { "shop_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_supplier_id_status",
                schema: "marketplace",
                table: "purchase_orders",
                columns: new[] { "supplier_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_ratings_order_id",
                schema: "marketplace",
                table: "ratings",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ratings_supplier_id",
                schema: "marketplace",
                table: "ratings",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_sharing_settings_shop_id",
                schema: "marketplace",
                table: "sharing_settings",
                column: "shop_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_suppliers_shop_id_supplier_id",
                schema: "marketplace",
                table: "shop_suppliers",
                columns: new[] { "shop_id", "supplier_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_suppliers_supplier_id",
                schema: "marketplace",
                table: "shop_suppliers",
                column: "supplier_id");

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
                name: "deals",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "group_commitments",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "last_prices",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "marketplace_settings",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "ratings",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "sharing_settings",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "shop_suppliers",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "supplier_areas",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "supplier_products",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "group_deals",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "marketplace");
        }
    }
}
