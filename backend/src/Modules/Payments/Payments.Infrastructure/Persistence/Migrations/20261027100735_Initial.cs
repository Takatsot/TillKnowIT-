using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "disputes",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    raised_by_person_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    raised_by = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    resolved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disputes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "payments",
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
                name: "payments",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    partner_ref = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    checkout_url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    held_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    due_cents = table.Column<long>(type: "bigint", nullable: true),
                    release_due_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    frozen = table.Column<bool>(type: "bit", nullable: false),
                    released_cents = table.Column<long>(type: "bigint", nullable: false),
                    refunded_cents = table.Column<long>(type: "bigint", nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_runs",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    matched = table.Column<int>(type: "int", nullable: false),
                    mismatches_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_receipts",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    partner = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    event_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_audit",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    actor_person_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_audit", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_audit_payment_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "payments",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_order_id_status",
                schema: "payments",
                table: "disputes",
                columns: new[] { "order_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_status",
                schema: "payments",
                table: "disputes",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "payments",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_audit_payment_id",
                schema: "payments",
                table: "payment_audit",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_order_id",
                schema: "payments",
                table: "payments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_partner_ref",
                schema: "payments",
                table: "payments",
                column: "partner_ref",
                unique: true,
                filter: "[partner_ref] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payments_status_release_due_at",
                schema: "payments",
                table: "payments",
                columns: new[] { "status", "release_due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_runs_day",
                schema: "payments",
                table: "reconciliation_runs",
                column: "day");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_receipts_partner_event_id",
                schema: "payments",
                table: "webhook_receipts",
                columns: new[] { "partner", "event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "disputes",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_audit",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "reconciliation_runs",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "webhook_receipts",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "payments");
        }
    }
}
