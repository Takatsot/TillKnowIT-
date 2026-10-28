using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "accounting");

            migrationBuilder.CreateTable(
                name: "account_mappings",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    key = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    account_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_mappings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_connections",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    token_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    tenant_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    disconnected_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounting_connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "export_runs",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    last_error = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    external_reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    owner_told = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "accounting",
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
                name: "ix_account_mappings_shop_id_key",
                schema: "accounting",
                table: "account_mappings",
                columns: new[] { "shop_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounting_connections_shop_id",
                schema: "accounting",
                table: "accounting_connections",
                column: "shop_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_export_runs_shop_id_provider_day",
                schema: "accounting",
                table: "export_runs",
                columns: new[] { "shop_id", "provider", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "accounting",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_mappings",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "accounting_connections",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "export_runs",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "accounting");
        }
    }
}
