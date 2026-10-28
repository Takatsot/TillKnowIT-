using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "credit_applications",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    partner_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    requested_cents = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    partner_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    approved_cents = table.Column<long>(type: "bigint", nullable: false),
                    term_days = table.Column<int>(type: "int", nullable: false),
                    decision_reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_consents",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    partner_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    months = table.Column<int>(type: "int", nullable: false),
                    granted_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_consents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_draws",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    application_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    principal_cents = table.Column<long>(type: "bigint", nullable: false),
                    repay_cents = table.Column<long>(type: "bigint", nullable: false),
                    repaid_cents = table.Column<long>(type: "bigint", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    partner_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_draws", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_repayments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    draw_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_repayments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_shares",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    partner_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    months = table.Column<int>(type: "int", nullable: false),
                    total_sales_cents = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_shares", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "finance",
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

            migrationBuilder.CreateIndex(
                name: "ix_credit_applications_shop_id",
                schema: "finance",
                table: "credit_applications",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_consents_shop_id_partner_code",
                schema: "finance",
                table: "credit_consents",
                columns: new[] { "shop_id", "partner_code" },
                unique: true,
                filter: "[withdrawn_at] IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_credit_draws_application_id",
                schema: "finance",
                table: "credit_draws",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_draws_order_id",
                schema: "finance",
                table: "credit_draws",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_draws_partner_reference",
                schema: "finance",
                table: "credit_draws",
                column: "partner_reference");

            migrationBuilder.CreateIndex(
                name: "ix_credit_draws_shop_id",
                schema: "finance",
                table: "credit_draws",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_repayments_draw_id",
                schema: "finance",
                table: "credit_repayments",
                column: "draw_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_repayments_reference",
                schema: "finance",
                table: "credit_repayments",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_shares_shop_id_created_at",
                schema: "finance",
                table: "data_shares",
                columns: new[] { "shop_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "finance",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_applications",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "credit_consents",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "credit_draws",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "credit_repayments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "data_shares",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "finance");
        }
    }
}
