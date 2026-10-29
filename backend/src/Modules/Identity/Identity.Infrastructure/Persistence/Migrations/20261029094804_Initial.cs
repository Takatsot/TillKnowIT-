using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "organisations",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    municipality_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organisations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "otp_challenges",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    person_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    phone_key = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    code_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "identity",
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
                name: "people",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    preferred_language = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    wipe_requested = table.Column<bool>(type: "bit", nullable: false),
                    enrolment_code_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    enrolled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_devices_organisations_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "identity",
                        principalTable: "organisations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shop_setup_steps",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    item = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    completed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_setup_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_shop_setup_steps_organisations_shop_id",
                        column: x => x.shop_id,
                        principalSchema: "identity",
                        principalTable: "organisations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    person_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    pin_hash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    failed_pin_attempts = table.Column<int>(type: "int", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_memberships_organisations_organisation_id",
                        column: x => x.organisation_id,
                        principalSchema: "identity",
                        principalTable: "organisations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_memberships_people_person_id",
                        column: x => x.person_id,
                        principalSchema: "identity",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_memberships_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "identity",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    permission_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_code });
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "roles",
                columns: new[] { "id", "code", "created_at", "created_by", "device_id", "is_active", "names", "sort_order" },
                values: new object[,]
                {
                    { new Guid("348e6554-ea72-2399-652b-d01e00d0d3db"), "field_agent", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Field agent\"}", 4 },
                    { new Guid("63135645-6d68-f7b8-72e0-710926a3c998"), "owner", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Owner\"}", 1 },
                    { new Guid("8668a93b-f5d5-1d8a-fdc4-27a74ec41a59"), "supplier_rep", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Supplier sales rep\"}", 7 },
                    { new Guid("b10731c0-eb8c-60f3-8a5c-88f239d2a910"), "supplier_admin", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Supplier admin\"}", 6 },
                    { new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005"), "manager", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Manager\"}", 2 },
                    { new Guid("df96efb0-9ce8-44d4-6592-1fe7d95bb4ad"), "cashier", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Cashier\"}", 3 },
                    { new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df"), "platform_admin", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, true, "{\"en\":\"Platform admin\"}", 5 }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "role_permissions",
                columns: new[] { "permission_code", "role_id" },
                values: new object[,]
                {
                    { "device.manage", new Guid("348e6554-ea72-2399-652b-d01e00d0d3db") },
                    { "shop.setup", new Guid("348e6554-ea72-2399-652b-d01e00d0d3db") },
                    { "shop.verify", new Guid("348e6554-ea72-2399-652b-d01e00d0d3db") },
                    { "staff.manage", new Guid("348e6554-ea72-2399-652b-d01e00d0d3db") },
                    { "cashup.do", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "compliance.manage", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "credit.manage", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "credit.payment", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "device.manage", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "product.edit", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "report.view", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "sale.cancel", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "sale.create", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "sale.override_expiry", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "sale.price_override", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "sale.refund", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "sale.refund_approve", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "shop.settings", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "slip.send", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "staff.manage", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "stock.count", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "stock.receive", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "vas.sell", new Guid("63135645-6d68-f7b8-72e0-710926a3c998") },
                    { "deals.manage", new Guid("8668a93b-f5d5-1d8a-fdc4-27a74ec41a59") },
                    { "supplier.catalogue", new Guid("8668a93b-f5d5-1d8a-fdc4-27a74ec41a59") },
                    { "supplier.orders", new Guid("8668a93b-f5d5-1d8a-fdc4-27a74ec41a59") },
                    { "deals.manage", new Guid("b10731c0-eb8c-60f3-8a5c-88f239d2a910") },
                    { "supplier.catalogue", new Guid("b10731c0-eb8c-60f3-8a5c-88f239d2a910") },
                    { "supplier.demand", new Guid("b10731c0-eb8c-60f3-8a5c-88f239d2a910") },
                    { "supplier.manage", new Guid("b10731c0-eb8c-60f3-8a5c-88f239d2a910") },
                    { "supplier.orders", new Guid("b10731c0-eb8c-60f3-8a5c-88f239d2a910") },
                    { "supplier.reps", new Guid("b10731c0-eb8c-60f3-8a5c-88f239d2a910") },
                    { "supplier.statements", new Guid("b10731c0-eb8c-60f3-8a5c-88f239d2a910") },
                    { "cashup.do", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "compliance.manage", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "credit.manage", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "credit.payment", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "product.edit", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "report.view", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "sale.cancel", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "sale.create", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "sale.override_expiry", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "sale.price_override", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "sale.refund", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "sale.refund_approve", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "slip.send", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "stock.count", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "stock.receive", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "vas.sell", new Guid("bbdebcea-b24c-c7e1-9d0d-583dfcf15005") },
                    { "cashup.do", new Guid("df96efb0-9ce8-44d4-6592-1fe7d95bb4ad") },
                    { "credit.payment", new Guid("df96efb0-9ce8-44d4-6592-1fe7d95bb4ad") },
                    { "sale.create", new Guid("df96efb0-9ce8-44d4-6592-1fe7d95bb4ad") },
                    { "sale.refund", new Guid("df96efb0-9ce8-44d4-6592-1fe7d95bb4ad") },
                    { "slip.send", new Guid("df96efb0-9ce8-44d4-6592-1fe7d95bb4ad") },
                    { "vas.sell", new Guid("df96efb0-9ce8-44d4-6592-1fe7d95bb4ad") },
                    { "billing.manage", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") },
                    { "catalogue.approve", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") },
                    { "dispute.resolve", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") },
                    { "insights.manage", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") },
                    { "lookup.manage", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") },
                    { "marketplace.settings", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") },
                    { "pack.manage", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") },
                    { "partner.manage", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") },
                    { "school.manage", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") },
                    { "shop.verify", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") },
                    { "supplier.verify", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") },
                    { "user.manage", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") },
                    { "wallet.admin", new Guid("f53edd22-7d1f-7628-e3a5-f713240fb3df") }
                });

            migrationBuilder.CreateIndex(
                name: "ix_devices_shop_id",
                schema: "identity",
                table: "devices",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_organisation_id",
                schema: "identity",
                table: "memberships",
                column: "organisation_id");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_person_id_organisation_id",
                schema: "identity",
                table: "memberships",
                columns: new[] { "person_id", "organisation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memberships_role_id",
                schema: "identity",
                table: "memberships",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_organisations_parent_id",
                schema: "identity",
                table: "organisations",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_phone_key_created_at",
                schema: "identity",
                table: "otp_challenges",
                columns: new[] { "phone_key", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "identity",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_people_phone",
                schema: "identity",
                table: "people",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_code",
                schema: "identity",
                table: "roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_setup_steps_shop_id_item",
                schema: "identity",
                table: "shop_setup_steps",
                columns: new[] { "shop_id", "item" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "devices",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "memberships",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "otp_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "shop_setup_steps",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "people",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "organisations",
                schema: "identity");
        }
    }
}
