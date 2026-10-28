using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Insights.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "insights");

            migrationBuilder.CreateTable(
                name: "daily_area_aggregates",
                schema: "insights",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    area = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    units = table.Column<int>(type: "int", nullable: false),
                    revenue_cents = table.Column<long>(type: "bigint", nullable: false),
                    shop_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_area_aggregates", x => x.id);
                    table.CheckConstraint("ck_daily_area_aggregates_min_shops", "[shop_count] >= 10");
                });

            migrationBuilder.CreateTable(
                name: "insight_opt_ins",
                schema: "insights",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    opted_in = table.Column<bool>(type: "bit", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    changed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_insight_opt_ins", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "insights",
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
                name: "ix_daily_area_aggregates_area_category_day",
                schema: "insights",
                table: "daily_area_aggregates",
                columns: new[] { "area", "category", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_insight_opt_ins_shop_id",
                schema: "insights",
                table: "insight_opt_ins",
                column: "shop_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "insights",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_area_aggregates",
                schema: "insights");

            migrationBuilder.DropTable(
                name: "insight_opt_ins",
                schema: "insights");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "insights");
        }
    }
}
