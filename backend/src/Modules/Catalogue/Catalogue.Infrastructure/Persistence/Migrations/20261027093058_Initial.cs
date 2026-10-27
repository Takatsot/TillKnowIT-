using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Catalogue.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.EnsureSchema(
                name: "catalogue");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    detail_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "catalogue_products",
                schema: "catalogue",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    barcode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    size = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    category_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    photo_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    merged_into_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    review_note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogue_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feature_flags",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lookups",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    names = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lookups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "catalogue",
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

            migrationBuilder.InsertData(
                schema: "platform",
                table: "lookups",
                columns: new[] { "id", "code", "created_at", "created_by", "device_id", "is_active", "names", "sort_order", "type" },
                values: new object[,]
                {
                    { new Guid("01694459-66e1-264c-13a7-2809a988006c"), "damaged", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Damaged\"}", 3, "adjustment_reasons" },
                    { new Guid("05a4ab58-3431-abff-50a3-1c36ce642e61"), "baby", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Baby products\"}", 9, "categories" },
                    { new Guid("08488c61-e35a-0cbd-ec85-02238512de5b"), "household", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Household and cleaning\"}", 8, "categories" },
                    { new Guid("0cd9f10c-aca4-7e90-51ef-149455661181"), "cpt", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"City of Cape Town\"}", 4, "municipalities" },
                    { new Guid("109aa836-959f-e318-0d95-970b5ff375a3"), "drinks", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Cold drinks\"}", 3, "categories" },
                    { new Guid("1275fefe-e6db-77db-1d3e-591aa6f1e2ee"), "nmb", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Nelson Mandela Bay\"}", 6, "municipalities" },
                    { new Guid("18031aed-f4e0-e06f-9fa3-9505979ffb57"), "fire_certificate", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Fire certificate\"}", 4, "document_types" },
                    { new Guid("1c80024b-ea6b-939e-451b-405d265a874b"), "ekh", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"City of Ekurhuleni\"}", 3, "municipalities" },
                    { new Guid("21d59059-5ac8-eb08-7027-a755c6ebeba0"), "en", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"English\"}", 1, "languages" },
                    { new Guid("23ce43bc-cf37-47d0-df73-aee55f72f9c7"), "missing", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Missing\"}", 4, "adjustment_reasons" },
                    { new Guid("2832e09e-fce7-ea33-f40d-ac71173d100c"), "other", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Other document\"}", 5, "document_types" },
                    { new Guid("2e83a7e1-d322-9e24-f953-bec02349bef7"), "dairy_eggs", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Dairy and eggs\"}", 2, "categories" },
                    { new Guid("31275fec-2408-5f05-2ba0-a34c6b663fb0"), "jhb", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"City of Johannesburg\"}", 1, "municipalities" },
                    { new Guid("349e2492-cbde-8a87-fcda-9feac2fa741d"), "man", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Mangaung\"}", 8, "municipalities" },
                    { new Guid("49120fdd-2151-4f49-9d49-85770dffc225"), "af", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Afrikaans\"}", 6, "languages" },
                    { new Guid("5086312f-3e5c-4b1f-4ed7-043018b5ed48"), "own_use", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Own use\"}", 5, "adjustment_reasons" },
                    { new Guid("5ca7a982-7508-6501-ccee-d39c2c80fe17"), "each", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Each\"}", 1, "units" },
                    { new Guid("5f2986db-6625-dfec-8831-52f13b61fc6a"), "health_certificate", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Certificate of acceptability\"}", 2, "document_types" },
                    { new Guid("697cbc63-da73-8276-fa91-f72b01646193"), "tsh", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"City of Tshwane\"}", 2, "municipalities" },
                    { new Guid("69881fd9-1bdd-3a75-1d6e-49baa9ecbadc"), "cash", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Cash\"}", 1, "payment_methods" },
                    { new Guid("757c6c13-d2f0-ee68-ad34-895b4b4d5a46"), "buf", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Buffalo City\"}", 7, "municipalities" },
                    { new Guid("7904f4b3-fc0a-00c6-9fad-684f342b048f"), "toiletries", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Toiletries\"}", 7, "categories" },
                    { new Guid("79eefc90-7f0c-246c-653a-20477691b0ca"), "tobacco", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Tobacco\"}", 10, "categories" },
                    { new Guid("7be57393-a47a-5713-2952-a1655f82c4df"), "eth", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"eThekwini (Durban)\"}", 5, "municipalities" },
                    { new Guid("7d6134fd-f09e-9397-ec42-bec7eac0448d"), "counted", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Counted\"}", 1, "adjustment_reasons" },
                    { new Guid("8319a58b-7da1-c7f9-79ad-843ae3a7dbd1"), "pack", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Pack\"}", 2, "units" },
                    { new Guid("8c4038d0-55d0-02d5-b19a-18c7f5cd6ca3"), "tn", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Setswana\"}", 5, "languages" },
                    { new Guid("93654035-a1dc-1a3f-ccbf-8669d55dce3e"), "xh", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"isiXhosa\"}", 3, "languages" },
                    { new Guid("93fbf11c-91b0-50a6-8f77-cbe06d84722b"), "kg", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Kilogram\"}", 4, "units" },
                    { new Guid("9739f1e4-4169-d900-2ada-5327616d2e86"), "groceries", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Groceries and staples\"}", 5, "categories" },
                    { new Guid("abd6dcad-2e5c-f707-8677-35289c36d0b9"), "food_permit", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Food handling permit\"}", 3, "document_types" },
                    { new Guid("b0ad82ad-053f-7110-8c18-2cc9ac9e9381"), "frozen", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Frozen food\"}", 6, "categories" },
                    { new Guid("c14190fd-5118-f48f-0a7f-11e38b538bbe"), "qr", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"QR payment\"}", 3, "payment_methods" },
                    { new Guid("c4bbab6a-0ab9-985a-4162-73b6a73e5984"), "zu", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"isiZulu\"}", 2, "languages" },
                    { new Guid("cc004e1b-ee85-a56b-3e81-62b160ce69b0"), "st", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Sesotho\"}", 4, "languages" },
                    { new Guid("d04d8cfa-b4cd-841a-39c6-ccf300251043"), "other", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Other\"}", 11, "categories" },
                    { new Guid("d8ac1f59-2ab0-6fdb-6298-aea0adce1229"), "trading_licence", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Business or trading licence\"}", 1, "document_types" },
                    { new Guid("db734afc-4460-6100-b143-a842922a73e6"), "card", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Card\"}", 2, "payment_methods" },
                    { new Guid("dd6f2c0a-1783-5741-c96f-6141a674620d"), "vat_0", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Zero rated\"}", 2, "vat_rates" },
                    { new Guid("e3a49154-4340-b773-8729-6c806bd8d748"), "vat_15", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"VAT 15%\"}", 1, "vat_rates" },
                    { new Guid("ed7e4c08-af61-3180-ae6e-a2f9ab751a52"), "bread_bakery", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Bread and bakery\"}", 1, "categories" },
                    { new Guid("f30a145f-2aac-9ddc-5343-8fcb263375fc"), "case", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Case\"}", 3, "units" },
                    { new Guid("f313c043-b3da-8a5a-ab89-7f79402a60e2"), "litre", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Litre\"}", 5, "units" },
                    { new Guid("f99dead7-4754-0caf-a49c-9bba4fdcc3cd"), "snacks_sweets", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Snacks and sweets\"}", 4, "categories" },
                    { new Guid("fdac4de4-1107-6723-46cf-6f8fe5c8e9bc"), "expired", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Expired\"}", 2, "adjustment_reasons" },
                    { new Guid("ff2bc0dc-a399-a97c-2996-e522eb4a2158"), "credit", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Credit book\"}", 4, "payment_methods" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entity_type_entity_id",
                schema: "platform",
                table: "audit_log",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_catalogue_products_barcode",
                schema: "catalogue",
                table: "catalogue_products",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalogue_products_status",
                schema: "catalogue",
                table: "catalogue_products",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_feature_flags_key",
                schema: "platform",
                table: "feature_flags",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lookups_type_code",
                schema: "platform",
                table: "lookups",
                columns: new[] { "type", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "catalogue",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "catalogue_products",
                schema: "catalogue");

            migrationBuilder.DropTable(
                name: "feature_flags",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "lookups",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "catalogue");
        }
    }
}
