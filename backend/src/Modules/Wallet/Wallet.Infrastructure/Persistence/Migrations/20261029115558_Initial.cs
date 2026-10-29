using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Wallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "wallet");

            migrationBuilder.CreateTable(
                name: "break_slots",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    school_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    cut_off = table.Column<TimeOnly>(type: "time", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_break_slots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "erasure_requests",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    holder_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    due_by = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_erasure_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guardianships",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_person_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    holder_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    consent_version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    notify_on_purchase = table.Column<bool>(type: "bit", nullable: false),
                    low_balance_alert_cents = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guardianships", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holder_tokens",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    holder_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    blocked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holder_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_transactions",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    partner_ref = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "menu_items",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    school_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    price_cents = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    category_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    is_available = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "offline_sales",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    overdrawn = table.Column<bool>(type: "bit", nullable: false),
                    sold_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offline_sales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "wallet",
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
                name: "pre_orders",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    holder_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    school_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    break_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    collect_on = table.Column<DateOnly>(type: "date", nullable: false),
                    cut_off_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    lines_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    total_cents = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    purchase_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pre_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reconciliations",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    partner_total_cents = table.Column<long>(type: "bigint", nullable: false),
                    ledger_total_cents = table.Column<long>(type: "bigint", nullable: false),
                    difference_cents = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    resolved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refund_requests",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    school_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    partner_ref = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refund_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "school_admins",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    school_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    person_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_school_admins", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schools",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    emis_number = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    verified_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    payout_schedule = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    fee_basis_points = table.Column<int>(type: "int", nullable: false),
                    staff_accounts_enabled = table.Column<bool>(type: "bit", nullable: false),
                    daily_ceiling_cents = table.Column<long>(type: "bigint", nullable: true),
                    offline_split_minutes = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schools", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shop_payouts",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    partner_ref = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_payouts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "top_ups",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    partner_ref = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    checkout_url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_top_ups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_holders",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    school_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    first_name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    last_name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    grade = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    class_name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    photo_url = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    link_code_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_holders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_purchases",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    holder_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    staff_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    fee_cents = table.Column<long>(type: "bigint", nullable: false),
                    items_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    offline = table.Column<bool>(type: "bit", nullable: false),
                    sold_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_purchases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_restrictions",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_restrictions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    holder_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    school_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    daily_limit_cents = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_receipts",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    partner = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    event_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_nonzero", "[amount_cents] <> 0");
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalSchema: "wallet",
                        principalTable: "ledger_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "wallet",
                table: "ledger_accounts",
                columns: new[] { "id", "created_at", "created_by", "device_id", "owner_id", "type" },
                values: new object[,]
                {
                    { new Guid("63ecd483-e96e-c4c8-0337-fa12435c8689"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, "PartnerFloat" },
                    { new Guid("f3a46a7e-e0e9-649d-c25f-d997095496ab"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, null, "Fees" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_break_slots_school_id_code",
                schema: "wallet",
                table: "break_slots",
                columns: new[] { "school_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_erasure_requests_holder_id",
                schema: "wallet",
                table: "erasure_requests",
                column: "holder_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guardianships_holder_id",
                schema: "wallet",
                table: "guardianships",
                column: "holder_id");

            migrationBuilder.CreateIndex(
                name: "ix_guardianships_parent_person_id_holder_id",
                schema: "wallet",
                table: "guardianships",
                columns: new[] { "parent_person_id", "holder_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holder_tokens_holder_id",
                schema: "wallet",
                table: "holder_tokens",
                column: "holder_id");

            migrationBuilder.CreateIndex(
                name: "ix_holder_tokens_token_hash",
                schema: "wallet",
                table: "holder_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_account_id",
                schema: "wallet",
                table: "ledger_entries",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_transaction_id",
                schema: "wallet",
                table: "ledger_entries",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_transactions_reference",
                schema: "wallet",
                table: "ledger_transactions",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_menu_items_school_id",
                schema: "wallet",
                table: "menu_items",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_offline_sales_wallet_id_sold_at",
                schema: "wallet",
                table: "offline_sales",
                columns: new[] { "wallet_id", "sold_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "wallet",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pre_orders_holder_id",
                schema: "wallet",
                table: "pre_orders",
                column: "holder_id");

            migrationBuilder.CreateIndex(
                name: "ix_pre_orders_school_id_collect_on",
                schema: "wallet",
                table: "pre_orders",
                columns: new[] { "school_id", "collect_on" });

            migrationBuilder.CreateIndex(
                name: "ix_reconciliations_day",
                schema: "wallet",
                table: "reconciliations",
                column: "day");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliations_status",
                schema: "wallet",
                table: "reconciliations",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_refund_requests_wallet_id",
                schema: "wallet",
                table: "refund_requests",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_school_admins_school_id_person_id",
                schema: "wallet",
                table: "school_admins",
                columns: new[] { "school_id", "person_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_schools_shop_id",
                schema: "wallet",
                table: "schools",
                column: "shop_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_payouts_shop_id_day",
                schema: "wallet",
                table: "shop_payouts",
                columns: new[] { "shop_id", "day" });

            migrationBuilder.CreateIndex(
                name: "ix_top_ups_partner_ref",
                schema: "wallet",
                table: "top_ups",
                column: "partner_ref",
                unique: true,
                filter: "[partner_ref] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_holders_link_code_hash",
                schema: "wallet",
                table: "wallet_holders",
                column: "link_code_hash",
                unique: true,
                filter: "[link_code_hash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_holders_school_id",
                schema: "wallet",
                table: "wallet_holders",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_purchases_shop_id_sold_at",
                schema: "wallet",
                table: "wallet_purchases",
                columns: new[] { "shop_id", "sold_at" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_purchases_wallet_id_sold_at",
                schema: "wallet",
                table: "wallet_purchases",
                columns: new[] { "wallet_id", "sold_at" });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_restrictions_wallet_id",
                schema: "wallet",
                table: "wallet_restrictions",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallets_holder_id",
                schema: "wallet",
                table: "wallets",
                column: "holder_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wallets_school_id",
                schema: "wallet",
                table: "wallets",
                column: "school_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_receipts_partner_event_id",
                schema: "wallet",
                table: "webhook_receipts",
                columns: new[] { "partner", "event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "break_slots",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "erasure_requests",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "guardianships",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "holder_tokens",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "ledger_accounts",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "menu_items",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "offline_sales",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "pre_orders",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "reconciliations",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "refund_requests",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "school_admins",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "schools",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "shop_payouts",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "top_ups",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "wallet_holders",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "wallet_purchases",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "wallet_restrictions",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "wallets",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "webhook_receipts",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "ledger_transactions",
                schema: "wallet");
        }
    }
}
