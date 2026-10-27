using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopCore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shopcore");

            migrationBuilder.CreateTable(
                name: "cash_ups",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    till_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    float_cents = table.Column<long>(type: "bigint", nullable: false),
                    expected_cents = table.Column<long>(type: "bigint", nullable: false),
                    counted_cents = table.Column<long>(type: "bigint", nullable: false),
                    difference_cents = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    cashed_up_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_ups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "compliance_documents",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    file_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    original_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compliance_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_accounts",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    credit_limit_cents = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "floor_tables",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    area = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    seats = table.Column<int>(type: "int", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_floor_tables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gift_vouchers",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    code_last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_void = table.Column<bool>(type: "bit", nullable: false),
                    paid_with_method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    initial_cents = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_vouchers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_mappings",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    supplier_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    mapping_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_mappings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoice_imports",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    file_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    supplier_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    result_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_imports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kitchen_orders",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_no = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ticket_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    sale_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    table_label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    items_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ready_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kitchen_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "layby_plans",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    customer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    customer_phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    lines_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    total_cents = table.Column<long>(type: "bigint", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    cancellation_fee_cents = table.Column<long>(type: "bigint", nullable: false),
                    refund_due_cents = table.Column<long>(type: "bigint", nullable: false),
                    sale_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_layby_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "modifier_groups",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    required = table.Column<bool>(type: "bit", nullable: false),
                    min = table.Column<int>(type: "int", nullable: false),
                    max = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modifier_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "open_tickets",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    table_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    lines_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    held_by_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    held_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    closed_sale_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_tickets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "shopcore",
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
                name: "products",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    variant_label = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_add_on = table.Column<bool>(type: "bit", nullable: false),
                    requires_serial = table.Column<bool>(type: "bit", nullable: false),
                    sold_by_weight = table.Column<bool>(type: "bit", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    catalogue_product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    barcode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    category_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    cost_cents = table.Column<long>(type: "bigint", nullable: false),
                    price_cents = table.Column<long>(type: "bigint", nullable: false),
                    low_stock_level = table.Column<int>(type: "int", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "promotions",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    product_ids_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    percent = table.Column<int>(type: "int", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    items_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    days = table.Column<int>(type: "int", nullable: false),
                    from_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    to_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promotions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    customer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    customer_phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    lines_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    total_cents = table.Column<long>(type: "bigint", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ticket_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    till_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    staff_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    total_cents = table.Column<long>(type: "bigint", nullable: false),
                    vat_cents = table.Column<long>(type: "bigint", nullable: false),
                    payment_method_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    tendered_cents = table.Column<long>(type: "bigint", nullable: true),
                    change_cents = table.Column<long>(type: "bigint", nullable: true),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    sold_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tip_cents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shop_settings",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vas_sales",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    recipient = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    product_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    commission_cents = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    partner_ref = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    token = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    failure_reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vas_sales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_entries",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    sale_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    payment_method_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_entries_credit_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "shopcore",
                        principalTable: "credit_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "voucher_entries",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    sale_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_voucher_entries_gift_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalSchema: "shopcore",
                        principalTable: "gift_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "layby_payments",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    method_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_layby_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_layby_payments_layby_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "shopcore",
                        principalTable: "layby_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "modifier_options",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modifier_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_modifier_options_modifier_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "shopcore",
                        principalTable: "modifier_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_modifier_options_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "shopcore",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_modifier_groups",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_modifier_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_modifier_groups_modifier_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "shopcore",
                        principalTable: "modifier_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_modifier_groups_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "shopcore",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "serial_numbers",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    serial = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    sale_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sold_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serial_numbers", x => x.id);
                    table.ForeignKey(
                        name: "fk_serial_numbers_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "shopcore",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    sale_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    unit_cost_cents = table.Column<long>(type: "bigint", nullable: true),
                    reason_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    supplier_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    invoice_ref = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_movements_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "shopcore",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_cancellations",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sale_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    staff_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_cancellations", x => x.id);
                    table.ForeignKey(
                        name: "fk_sale_cancellations_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "shopcore",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_lines",
                schema: "shopcore",
                columns: table => new
                {
                    sale_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    price_cents = table.Column<long>(type: "bigint", nullable: false),
                    cost_cents = table.Column<long>(type: "bigint", nullable: false),
                    unit_divisor = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    discount_cents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    promotion_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    price_override_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    expiry_override_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_lines", x => new { x.sale_id, x.product_id });
                    table.ForeignKey(
                        name: "fk_sale_lines_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "shopcore",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sale_payments",
                schema: "shopcore",
                columns: table => new
                {
                    sale_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    method_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    tendered_cents = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_payments", x => new { x.sale_id, x.method_code });
                    table.ForeignKey(
                        name: "fk_sale_payments_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "shopcore",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sale_refunds",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sale_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    method_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    approved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_refunds", x => x.id);
                    table.ForeignKey(
                        name: "fk_sale_refunds_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "shopcore",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "slip_requests",
                schema: "shopcore",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sale_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    destination = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slip_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_slip_requests_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "shopcore",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_refund_lines",
                schema: "shopcore",
                columns: table => new
                {
                    refund_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_refund_lines", x => new { x.refund_id, x.product_id });
                    table.ForeignKey(
                        name: "fk_sale_refund_lines_sale_refunds_refund_id",
                        column: x => x.refund_id,
                        principalSchema: "shopcore",
                        principalTable: "sale_refunds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cash_ups_shop_id_till_device_id_day",
                schema: "shopcore",
                table: "cash_ups",
                columns: new[] { "shop_id", "till_device_id", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_compliance_documents_expires_on",
                schema: "shopcore",
                table: "compliance_documents",
                column: "expires_on");

            migrationBuilder.CreateIndex(
                name: "ix_credit_accounts_shop_id",
                schema: "shopcore",
                table: "credit_accounts",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_entries_account_id",
                schema: "shopcore",
                table: "credit_entries",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_entries_sale_id_type",
                schema: "shopcore",
                table: "credit_entries",
                columns: new[] { "sale_id", "type" },
                unique: true,
                filter: "sale_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_floor_tables_shop_id_area_name",
                schema: "shopcore",
                table: "floor_tables",
                columns: new[] { "shop_id", "area", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gift_vouchers_shop_id_code_hash",
                schema: "shopcore",
                table: "gift_vouchers",
                columns: new[] { "shop_id", "code_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_mappings_shop_id_supplier_key",
                schema: "shopcore",
                table: "import_mappings",
                columns: new[] { "shop_id", "supplier_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_imports_status_created_at",
                schema: "shopcore",
                table: "invoice_imports",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_kitchen_orders_shop_id_order_no",
                schema: "shopcore",
                table: "kitchen_orders",
                columns: new[] { "shop_id", "order_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kitchen_orders_shop_id_status_created_at",
                schema: "shopcore",
                table: "kitchen_orders",
                columns: new[] { "shop_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_layby_payments_device_id_paid_at",
                schema: "shopcore",
                table: "layby_payments",
                columns: new[] { "device_id", "paid_at" });

            migrationBuilder.CreateIndex(
                name: "ix_layby_payments_plan_id",
                schema: "shopcore",
                table: "layby_payments",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_layby_plans_shop_id_reference",
                schema: "shopcore",
                table: "layby_plans",
                columns: new[] { "shop_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_layby_plans_status_due_on",
                schema: "shopcore",
                table: "layby_plans",
                columns: new[] { "status", "due_on" });

            migrationBuilder.CreateIndex(
                name: "ix_modifier_groups_shop_id",
                schema: "shopcore",
                table: "modifier_groups",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_modifier_options_group_id_product_id",
                schema: "shopcore",
                table: "modifier_options",
                columns: new[] { "group_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_modifier_options_product_id",
                schema: "shopcore",
                table: "modifier_options",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_open_tickets_shop_id_status",
                schema: "shopcore",
                table: "open_tickets",
                columns: new[] { "shop_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "shopcore",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_product_modifier_groups_group_id",
                schema: "shopcore",
                table: "product_modifier_groups",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_modifier_groups_product_id_group_id",
                schema: "shopcore",
                table: "product_modifier_groups",
                columns: new[] { "product_id", "group_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_shop_id_barcode",
                schema: "shopcore",
                table: "products",
                columns: new[] { "shop_id", "barcode" },
                unique: true,
                filter: "barcode IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_promotions_shop_id_is_active",
                schema: "shopcore",
                table: "promotions",
                columns: new[] { "shop_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_quotes_shop_id_number",
                schema: "shopcore",
                table: "quotes",
                columns: new[] { "shop_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_cancellations_sale_id",
                schema: "shopcore",
                table: "sale_cancellations",
                column: "sale_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_refunds_device_id_refunded_at",
                schema: "shopcore",
                table: "sale_refunds",
                columns: new[] { "device_id", "refunded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_refunds_sale_id",
                schema: "shopcore",
                table: "sale_refunds",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_customer_id",
                schema: "shopcore",
                table: "sales",
                column: "customer_id",
                filter: "customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_shop_id_sold_at",
                schema: "shopcore",
                table: "sales",
                columns: new[] { "shop_id", "sold_at" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_product_id",
                schema: "shopcore",
                table: "serial_numbers",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_sale_id",
                schema: "shopcore",
                table: "serial_numbers",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_shop_id_product_id_serial",
                schema: "shopcore",
                table: "serial_numbers",
                columns: new[] { "shop_id", "product_id", "serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_settings_shop_id_key",
                schema: "shopcore",
                table: "shop_settings",
                columns: new[] { "shop_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_slip_requests_sale_id",
                schema: "shopcore",
                table: "slip_requests",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_product_id",
                schema: "shopcore",
                table: "stock_movements",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_sale_id",
                schema: "shopcore",
                table: "stock_movements",
                column: "sale_id",
                filter: "sale_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_shop_id_product_id",
                schema: "shopcore",
                table: "stock_movements",
                columns: new[] { "shop_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vas_sales_shop_id_created_at",
                schema: "shopcore",
                table: "vas_sales",
                columns: new[] { "shop_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_entries_voucher_id_reference_type",
                schema: "shopcore",
                table: "voucher_entries",
                columns: new[] { "voucher_id", "reference", "type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_ups",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "compliance_documents",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "credit_entries",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "floor_tables",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "import_mappings",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "invoice_imports",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "kitchen_orders",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "layby_payments",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "modifier_options",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "open_tickets",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "product_modifier_groups",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "promotions",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "quotes",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "sale_cancellations",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "sale_lines",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "sale_payments",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "sale_refund_lines",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "serial_numbers",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "shop_settings",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "slip_requests",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "vas_sales",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "voucher_entries",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "credit_accounts",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "layby_plans",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "modifier_groups",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "sale_refunds",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "products",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "gift_vouchers",
                schema: "shopcore");

            migrationBuilder.DropTable(
                name: "sales",
                schema: "shopcore");
        }
    }
}
