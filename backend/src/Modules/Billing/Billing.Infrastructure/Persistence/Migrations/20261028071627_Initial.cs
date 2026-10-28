using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    metric = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    quantity = table.Column<long>(type: "bigint", nullable: false),
                    unit_cents = table.Column<long>(type: "bigint", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    reference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    organisation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    subtotal_cents = table.Column<long>(type: "bigint", nullable: false),
                    vat_percent = table.Column<int>(type: "int", nullable: false),
                    vat_cents = table.Column<long>(type: "bigint", nullable: false),
                    total_cents = table.Column<long>(type: "bigint", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "billing",
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
                name: "plan_features",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    feature = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    limit = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_features", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    audience = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    price_cents = table.Column<long>(type: "bigint", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sponsored_campaigns",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_organisation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    offer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    budget_cents = table.Column<long>(type: "bigint", nullable: false),
                    spent_cents = table.Column<long>(type: "bigint", nullable: false),
                    showing_cents = table.Column<long>(type: "bigint", nullable: false),
                    showings = table.Column<long>(type: "bigint", nullable: false),
                    areas = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sponsored_campaigns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    price_cents = table.Column<long>(type: "bigint", nullable: false),
                    billing_person_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    pending_plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    cancel_at_period_end = table.Column<bool>(type: "bit", nullable: false),
                    past_due_since = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    reminders_sent = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usage_records",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    metric = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    quantity = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    unit_cents = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_records", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "billing",
                table: "plan_features",
                columns: new[] { "id", "created_at", "created_by", "device_id", "feature", "limit", "plan_id" },
                values: new object[,]
                {
                    { new Guid("0125edb2-00e6-59df-9ee1-7096acae4f6c"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "supplier_analytics", null, new Guid("39b261de-0c73-ffa0-a449-4712c51d31a1") },
                    { new Guid("052be5c6-2b71-3afe-b3e6-8f7c3befa2c9"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "multi_branch", null, new Guid("9b46eb70-cbde-f148-c385-433dc4d74e5d") },
                    { new Guid("7c501d83-9d70-1ef3-c4e5-c87f6ea85cb2"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "messages", 500L, new Guid("9b46eb70-cbde-f148-c385-433dc4d74e5d") },
                    { new Guid("8399164c-e3a1-eac7-6a72-2e2e3ee5dceb"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "insight_reports", null, new Guid("39b261de-0c73-ffa0-a449-4712c51d31a1") },
                    { new Guid("8561b1a1-4314-3bcb-4ad3-d1acd8d043ba"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "supplier_analytics", null, new Guid("afc13ee2-356c-613e-20b1-de3c45de264d") },
                    { new Guid("9688fc1c-7963-43be-746d-8440a035dd60"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "insight_reports", null, new Guid("afc13ee2-356c-613e-20b1-de3c45de264d") },
                    { new Guid("a300a692-21ed-e619-a4cb-5d6c9ec9a0f6"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "messages", 50L, new Guid("959df19f-8f63-feda-a807-0e0257cd8b3e") },
                    { new Guid("a4e6a392-2c19-02f1-04fa-74131d1799a6"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "partner_api", null, new Guid("afc13ee2-356c-613e-20b1-de3c45de264d") },
                    { new Guid("aeb42aac-4b1a-b777-c51e-681c74c9e0ea"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "accounting_export", null, new Guid("9b46eb70-cbde-f148-c385-433dc4d74e5d") },
                    { new Guid("c1a08a92-74f4-0cf8-4eff-540bebe4bd64"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "sponsored", null, new Guid("afc13ee2-356c-613e-20b1-de3c45de264d") },
                    { new Guid("ce29bb3a-1722-193e-b818-ce4f4a4727c4"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "insight_reports", null, new Guid("9b46eb70-cbde-f148-c385-433dc4d74e5d") },
                    { new Guid("d405973f-934d-6abf-e0e2-ad7468b5b953"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "pay_later", null, new Guid("9b46eb70-cbde-f148-c385-433dc4d74e5d") }
                });

            migrationBuilder.InsertData(
                schema: "billing",
                table: "plans",
                columns: new[] { "id", "audience", "code", "created_at", "created_by", "device_id", "is_active", "name", "price_cents", "updated_at", "version" },
                values: new object[,]
                {
                    { new Guid("39b261de-0c73-ffa0-a449-4712c51d31a1"), "supplier", "supplier_plus", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Plus", 29900L, null, 1 },
                    { new Guid("538949b6-038a-5c3a-6202-8b659a066783"), "supplier", "supplier_free", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Free", 0L, null, 1 },
                    { new Guid("959df19f-8f63-feda-a807-0e0257cd8b3e"), "shop", "shop_free", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Free", 0L, null, 1 },
                    { new Guid("9b46eb70-cbde-f148-c385-433dc4d74e5d"), "shop", "shop_pro", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Pro", 19900L, null, 1 },
                    { new Guid("afc13ee2-356c-613e-20b1-de3c45de264d"), "supplier", "supplier_premium", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "Premium", 99900L, null, 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_invoice_id",
                schema: "billing",
                table: "invoice_lines",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_number",
                schema: "billing",
                table: "invoices",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_organisation_id_created_at",
                schema: "billing",
                table: "invoices",
                columns: new[] { "organisation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_reference",
                schema: "billing",
                table: "invoices",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "billing",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_plan_features_plan_id_feature",
                schema: "billing",
                table: "plan_features",
                columns: new[] { "plan_id", "feature" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plans_code",
                schema: "billing",
                table: "plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sponsored_campaigns_status_starts_on_ends_on",
                schema: "billing",
                table: "sponsored_campaigns",
                columns: new[] { "status", "starts_on", "ends_on" });

            migrationBuilder.CreateIndex(
                name: "ix_sponsored_campaigns_supplier_organisation_id",
                schema: "billing",
                table: "sponsored_campaigns",
                column: "supplier_organisation_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_organisation_id",
                schema: "billing",
                table: "subscriptions",
                column: "organisation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status_period_end",
                schema: "billing",
                table: "subscriptions",
                columns: new[] { "status", "period_end" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_organisation_id_invoice_id_occurred_at",
                schema: "billing",
                table: "usage_records",
                columns: new[] { "organisation_id", "invoice_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_organisation_id_reference",
                schema: "billing",
                table: "usage_records",
                columns: new[] { "organisation_id", "reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "plan_features",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "plans",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "sponsored_campaigns",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "subscriptions",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "usage_records",
                schema: "billing");
        }
    }
}
