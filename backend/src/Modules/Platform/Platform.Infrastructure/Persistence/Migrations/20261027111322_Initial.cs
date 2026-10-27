using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "business_packs",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    slip_template_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_packs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "platform",
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
                name: "shop_pack_changes",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    to_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_pack_changes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shop_packs",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pack_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    chosen_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    chosen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_packs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pack_features",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pack_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    flag = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    settings_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pack_features", x => x.id);
                    table.ForeignKey(
                        name: "fk_pack_features_business_packs_pack_id",
                        column: x => x.pack_id,
                        principalSchema: "platform",
                        principalTable: "business_packs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "platform",
                table: "business_packs",
                columns: new[] { "id", "code", "created_at", "created_by", "description", "device_id", "is_active", "name", "slip_template_code", "updated_at", "version" },
                values: new object[,]
                {
                    { new Guid("9232d895-4243-1d17-60ee-723ba511a2c5"), "salon", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Services with a duration, appointments, reminders and staff commission.", null, true, "Salon, barber or services", "slip.sale", null, 1 },
                    { new Guid("ad44ba06-f7cc-9495-9315-6fba07908bf0"), "butchery", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Sell by weight from a scale and scan the labels it prints.", null, true, "Butchery, deli or fresh produce", "slip.sale", null, 1 },
                    { new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), "cafe", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Tables, open tickets, a kitchen screen, add-ons, tips and promotions.", null, true, "Cafe, restaurant or takeaway", "slip.sale", null, 1 },
                    { new Guid("c12ef021-99f1-492f-b001-469dcc4200a2"), "spaza", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Sell, count stock, run a credit book and top up airtime. The simplest till.", null, true, "Spaza shop or general dealer", "slip.sale", null, 1 },
                    { new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), "clothing_hardware", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Sizes and colours, quotes, lay-by and serial numbers.", null, true, "Clothing or hardware shop", "slip.sale", null, 1 }
                });

            migrationBuilder.InsertData(
                schema: "platform",
                table: "pack_features",
                columns: new[] { "id", "created_at", "created_by", "device_id", "flag", "pack_id", "settings_json" },
                values: new object[,]
                {
                    { new Guid("0e15c9cc-562a-f0da-a2c7-dca932995b85"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "split_payments", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("1879d312-9b9f-06c5-57d4-0717402b750a"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "customers", new Guid("9232d895-4243-1d17-60ee-723ba511a2c5"), null },
                    { new Guid("210b56bc-91d7-67a5-185e-d65b380e64c3"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "price_embedded_barcodes", new Guid("ad44ba06-f7cc-9495-9315-6fba07908bf0"), "{\"prefix\":\"2\",\"itemDigits\":6,\"valueDigits\":5,\"valueType\":\"weight\",\"decimals\":3}" },
                    { new Guid("2eac3104-6cc4-810b-12c0-749c2422ebc7"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "variants", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("303f0528-52c6-e313-5eaa-69f0b1db4863"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "vouchers", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("3979f5b5-c7ae-3cce-691e-bab219b32d4c"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "printer", new Guid("9232d895-4243-1d17-60ee-723ba511a2c5"), null },
                    { new Guid("3fe53664-3a12-ed9a-0bdc-6e6b7adc66ee"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "refunds", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("41676aee-6ca0-865a-7075-69d522f34fbe"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "loyalty", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("439f5ae1-7188-d1cf-6202-ef06883e70b2"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "modifiers", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("446fb318-1c69-740f-66ea-73d48778defb"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "refunds", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("4bab9eb2-973b-3825-174d-0411481bdb88"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "customers", new Guid("ad44ba06-f7cc-9495-9315-6fba07908bf0"), null },
                    { new Guid("4e0ea512-cd6b-9079-d0bf-8a8b520d689c"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "cash_drawer", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("4ec17bd9-2c07-a3c1-0651-418e283cdede"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "cash_drawer", new Guid("ad44ba06-f7cc-9495-9315-6fba07908bf0"), null },
                    { new Guid("5bdb697d-b369-a416-3809-b0ca42f799b3"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "customers", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("5cbbd383-10ca-2737-4f8a-06a4f862c69b"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "cash_drawer", new Guid("9232d895-4243-1d17-60ee-723ba511a2c5"), null },
                    { new Guid("631d0999-81fa-4a27-ed5e-a3ca7ae1c8ed"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "printer", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("6327e839-1383-9cc2-5b5c-e50daa5e08b1"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "appointments", new Guid("9232d895-4243-1d17-60ee-723ba511a2c5"), null },
                    { new Guid("69ff9e6e-2a4e-d459-c1f1-d04775054491"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "loyalty", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("6ad1488e-ec01-3bd1-c3f6-62b1518729b7"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "split_payments", new Guid("ad44ba06-f7cc-9495-9315-6fba07908bf0"), null },
                    { new Guid("7331b9d2-e3ef-48d1-39f3-0bae6da44ea6"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "printer", new Guid("ad44ba06-f7cc-9495-9315-6fba07908bf0"), null },
                    { new Guid("7b7b0c32-8bb4-35a9-f369-300b07e1f4bd"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "message_bundles", new Guid("9232d895-4243-1d17-60ee-723ba511a2c5"), null },
                    { new Guid("7ff87f66-118e-0f00-2642-df946f6d5d24"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "message_bundles", new Guid("ad44ba06-f7cc-9495-9315-6fba07908bf0"), null },
                    { new Guid("87a16491-2eff-a99d-9d83-16e698967859"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "open_tickets", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("8b619def-5958-779a-9315-153d354bdf3c"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "customers", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("8c339532-915f-a40e-64a4-bc2998509b21"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "refunds", new Guid("ad44ba06-f7cc-9495-9315-6fba07908bf0"), null },
                    { new Guid("93af51f0-1a09-33b1-66a5-66b9965f5ae5"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "split_payments", new Guid("9232d895-4243-1d17-60ee-723ba511a2c5"), null },
                    { new Guid("949aad24-ef77-ba26-3db8-349cd9fc375d"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "weight", new Guid("ad44ba06-f7cc-9495-9315-6fba07908bf0"), null },
                    { new Guid("a05ad704-44d3-e0a7-b14f-14be553b8d35"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "promotions", new Guid("ad44ba06-f7cc-9495-9315-6fba07908bf0"), null },
                    { new Guid("a074b7a9-8ba2-bb0d-c706-9e130aece3b4"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "promotions", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("aa1b5e2c-7ae6-3a33-f65a-a1461369dd2a"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "vouchers", new Guid("9232d895-4243-1d17-60ee-723ba511a2c5"), null },
                    { new Guid("aa69c810-f990-0ba3-023c-3a8b85444dc2"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "cash_drawer", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("b6213dcc-b975-f058-30fe-a86045d192fe"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "layby", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("b623797b-9b76-06b6-07ee-632ed48b1b31"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "message_bundles", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("b84a38b0-8b82-b612-dddb-5e7becdcd5bd"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "commission", new Guid("9232d895-4243-1d17-60ee-723ba511a2c5"), null },
                    { new Guid("b94e9d15-5bbf-615e-ba6c-ddabe6783672"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "serial_numbers", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("c01d1a66-a98c-a301-59b4-20f03035e241"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "promotions", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("c421ae5e-34d5-b9f0-3fbe-cbea3a5e2283"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "tips", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("c6f68862-b37b-a6a5-a66d-ab6eec551aa9"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "message_bundles", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("c8a24fb1-ce77-f6b9-a920-8c00695ac2ce"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "tables", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("cb92f8a1-cd90-cb8c-e640-2df9b2acb006"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "vouchers", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("cf35e73c-9605-cf42-f1c5-41e2391bbeb6"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "quotes", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("d5483341-5656-69a4-723a-d2d21bee2550"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "loyalty", new Guid("9232d895-4243-1d17-60ee-723ba511a2c5"), null },
                    { new Guid("d8a0d1cc-c425-817e-b39c-c92e5db7e66a"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "scale", new Guid("ad44ba06-f7cc-9495-9315-6fba07908bf0"), null },
                    { new Guid("df3dc94b-500b-e146-dd9c-137ac041d76f"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "printer", new Guid("cb8f2f37-2794-6584-d100-89f5b0128b08"), null },
                    { new Guid("e0249321-9e00-9389-24fd-452ee901cf6c"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "split_payments", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null },
                    { new Guid("e301e07c-2f5c-efcc-9bd0-3ab807b4ed49"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "tips", new Guid("9232d895-4243-1d17-60ee-723ba511a2c5"), null },
                    { new Guid("e59ea537-d76b-47e5-b4cc-a9ecd8b58196"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "refunds", new Guid("9232d895-4243-1d17-60ee-723ba511a2c5"), null },
                    { new Guid("fccbf78d-5032-1476-820a-0fe26a0b61b9"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "kitchen", new Guid("b1bac7e8-e6a7-51ff-f7f1-26382da43412"), null }
                });

            migrationBuilder.CreateIndex(
                name: "ix_business_packs_code",
                schema: "platform",
                table: "business_packs",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "platform",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pack_features_pack_id_flag",
                schema: "platform",
                table: "pack_features",
                columns: new[] { "pack_id", "flag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_pack_changes_shop_id_created_at",
                schema: "platform",
                table: "shop_pack_changes",
                columns: new[] { "shop_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_packs_shop_id",
                schema: "platform",
                table: "shop_packs",
                column: "shop_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "pack_features",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "shop_pack_changes",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "shop_packs",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "business_packs",
                schema: "platform");
        }
    }
}
