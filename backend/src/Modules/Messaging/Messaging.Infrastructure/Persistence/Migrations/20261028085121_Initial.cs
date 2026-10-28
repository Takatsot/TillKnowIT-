using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Messaging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.CreateTable(
                name: "message_templates",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    language = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbound_messages",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    destination = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    template_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    shop_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    dedupe_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    provider_ref = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    cost_cents = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbound_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "messaging",
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
                schema: "messaging",
                table: "message_templates",
                columns: new[] { "id", "body", "channel", "code", "created_at", "created_by", "device_id", "language", "subject", "updated_at", "version" },
                values: new object[,]
                {
                    { new Guid("0192edb0-850e-0013-07af-1cca173f7e26"), "Hi {name},\n\nA reminder of your {service} on {date} at {time}.", "Email", "appointment.reminder", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", "Your appointment on {date}", null, 1 },
                    { new Guid("053bf730-0906-3522-3d4b-95fea84cf3f7"), "{shop}: your {document} expired on {date}. Renew it as soon as possible.", "Email", "compliance.reminder.0", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("058e98f4-6ad4-a6a7-ac28-5280a69ae317"), "Thank you for shopping at {shop}.\n\n{lines}\n\nTotal: {total}\nDate: {date}\n\nView your slip: {url}", "Email", "slip.sale", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", "Your slip from {shop}", null, 1 },
                    { new Guid("073062e0-7371-d194-cb17-d050c1ecd3db"), "{shop}: your {document} expires in {days} days, on {date}. Renew it now.", "Push", "compliance.reminder.7", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("0f363cd0-c727-a40a-77b4-b8e7c9d454c6"), "{shop}: your {document} expires on {date}. Please renew it in good time.", "Email", "compliance.reminder.30", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("17cd682e-72a1-2ad0-d4eb-45cec32719a2"), "{supplier} replied to order {order} ({shop}). {summary}\nTotal: {total}.", "Whatsapp", "order.update", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("182d45de-8512-18ad-61ea-125e4008aee0"), "Hi {name}, a reminder of your {service} on {date} at {time}.", "Whatsapp", "appointment.reminder", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("1bfed125-1154-39df-09d7-41e07b4a1400"), "{shop}: the till '{device}' has not synced for {hours} hours.", "Push", "device.offline", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("215e5faa-6b8a-edc6-81d0-2018089583ba"), "Hi {name}, your {plan} extras are paused until the payment is made. Your till is not affected.", "Email", "billing.limited", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", "Your TillKnowIT extras are paused", null, 1 },
                    { new Guid("276f84e2-6f58-8f14-bac1-50affb3270e7"), "Hi {name}, a reminder of your {service} on {date} at {time}.", "Sms", "appointment.reminder", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("310a164f-7686-5d48-128f-455f738c722b"), "{shop}: the till '{device}' has not synced for {hours} hours.", "Email", "device.offline", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("329a87f9-0e8b-fbe9-346a-aaafd7976556"), "{child} bought {amount} while the tuckshop was offline and the wallet did not have enough. Please top it up.", "Push", "wallet.overdrawn", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("452e2653-374d-6a32-2369-5b3800afb001"), "{shop}: your {document} expires in {days} days, on {date}. Renew it now.", "Whatsapp", "compliance.reminder.7", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("46aaa962-2f90-8cb6-f79a-194431e1796d"), "Thank you for shopping at {shop}.\n{lines}\nTotal: {total}\n{date}\nYour slip: {url}", "Whatsapp", "slip.sale", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("488e1a63-ca2a-f9f7-911d-079c2473dacf"), "Hi {name}, your {plan} extras are paused until the payment is made. Your till is not affected.", "Whatsapp", "billing.limited", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("4a31e132-4865-7bc9-eabe-ebb510c7e759"), "{shop}: you have used {used} of your {limit} messages this month. Slips can still be shown as a QR code.", "Whatsapp", "bundle.warning", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("51c23d1d-66d4-2856-e02a-a12c50bd4b19"), "{child} bought {amount} while the tuckshop was offline and the wallet did not have enough. Please top it up.", "Whatsapp", "wallet.overdrawn", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("539baf4f-112e-9165-25bb-e69e7ad6d197"), "{shop} today: sales {sales}, profit {profit}, {customers} customers. Low stock: {low}. Expiring soon: {expiring}. Cash-up: {cashup}. Cancellations: {cancellations}.", "Email", "daily.summary", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", "Daily summary for {shop}", null, 1 },
                    { new Guid("541ec247-dbc4-af63-966f-448139ff8713"), "{shop}: your {limit} messages for this month are used up. Slips are shown as a QR code until next month, or upgrade your plan.", "Whatsapp", "bundle.exhausted", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("55505a20-9e06-e40a-f65d-885db42183dc"), "{child}'s wallet is running low: {balance} left. Top up so they can buy at break.", "Whatsapp", "wallet.low_balance", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("55739b05-dd74-0d64-6f80-2f7f42f41c9d"), "{supplier} replied to order {order} ({shop}). {summary}\nTotal: {total}.", "Email", "order.update", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("5c48c668-cc7f-f123-1b33-4380e833a0cc"), "A tuckshop sale of {amount} could not be settled ({reason}).", "Email", "wallet.sale_rejected", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("6a385c11-81e9-90c5-2e93-b39d7df1aecb"), "Hi {name}, we could not take your {plan} payment of {total}. Please update your payment. Your extra features pause in {days} days; your till keeps working.", "Whatsapp", "billing.reminder", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("6a6f3a6a-ae0c-a1d0-d1aa-b678b53ea6ed"), "A tuckshop sale of {amount} could not be settled ({reason}).", "Whatsapp", "wallet.sale_rejected", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("6c43dd65-2c14-1894-b3d3-234424d932f4"), "Alert for {shop}: {message}", "Email", "alert.loss", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("6c7a9b11-b91b-04a2-5906-a262aca989c5"), "{amount} was added to {child}'s wallet. Balance: {balance}.", "Push", "wallet.topup", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("73c5b36f-b779-adad-b898-ef6772d4a41e"), "Alert for {shop}: {message}", "Whatsapp", "alert.loss", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("743083d3-4b10-9372-b4c6-9df6894441b6"), "{child} spent {amount} at the tuckshop. Balance: {balance}.", "Push", "wallet.purchase", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("75cb284f-55d8-2fa8-a167-ce06a058264b"), "{shop}: {total} paid on {date}. Slip: {url}", "Sms", "slip.sale", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("7611294a-09e6-278e-9f13-f42246e2fd87"), "{shop}: your {document} expired on {date}. Renew it as soon as possible.", "Whatsapp", "compliance.reminder.0", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("798e0704-8c27-eeff-3c13-37500c751995"), "{shop}: we could not send {day} to your accounting package ({error}). Open the app to fix it and try again.", "Whatsapp", "accounting.failed", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("7b630b6f-09f9-5e2a-3d78-46b41ec9f533"), "Hi {name}, we could not take your {plan} payment of {total}. Please update your payment. Your extra features pause in {days} days; your till keeps working.", "Email", "billing.reminder", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", "Your TillKnowIT payment did not go through", null, 1 },
                    { new Guid("8fbe44ef-b850-abb1-b543-92a329b55a86"), "{shop} has sent you order {order}:\n\n{lines}\n\nPayment: {payment}.\n\nPlease reply with what you can deliver and when.", "Email", "order.new", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", "New order {order} from {shop}", null, 1 },
                    { new Guid("9119c1c7-10d8-7447-d51e-0d463db651a7"), "{shop}: your {document} expires on {date}. Please renew it in good time.", "Push", "compliance.reminder.30", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("94845c7f-a5ca-33b3-4fdc-0b333cac208a"), "{shop}: your {document} expired on {date}. Renew it as soon as possible.", "Push", "compliance.reminder.0", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("9bb9bd4e-722c-39f9-b98e-0d9a9760ec76"), "{shop}: group deal for {product} from {supplier}. {summary}", "Email", "group.update", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("9e248c1a-4948-2a6e-0616-7d107053b96d"), "Alert for {shop}: {message}", "Push", "alert.loss", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("9fa89d6b-8e57-4250-cbd8-b0b2e99685bc"), "{shop} today: sales {sales}, profit {profit}, {customers} customers. Low stock: {low}. Expiring soon: {expiring}. Cash-up: {cashup}. Cancellations: {cancellations}.", "Push", "daily.summary", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("a211b0d1-208c-8fc4-c7af-8e81e47576f5"), "New order {order} from {shop}:\n{lines}\nPayment: {payment}.\nPlease reply with what you can deliver and when.", "Whatsapp", "order.new", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("add82f2b-f9ad-3d61-4474-caa8bde309d6"), "{shop}: your {document} expires on {date}. Please renew it in good time.", "Whatsapp", "compliance.reminder.30", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("c315586b-7e34-1123-d228-cc52e67fa6a1"), "{shop}: group deal for {product} from {supplier}. {summary}", "Whatsapp", "group.update", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("cfbf27e9-401d-06b1-451c-79277314562d"), "{shop}: your {document} expires in {days} days, on {date}. Renew it now.", "Email", "compliance.reminder.7", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("dd80b1f6-6b90-7164-ee67-546ba91565ae"), "Hi {name},\n\nInvoice {number} is {status}. Total {total} (VAT {vat}), due {due}.", "Email", "invoice.issued", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", "Invoice {number} from TillKnowIT", null, 1 },
                    { new Guid("e7e00541-9402-cb18-0be8-0dd74bfa85e1"), "{shop}: the till '{device}' has not synced for {hours} hours.", "Whatsapp", "device.offline", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("e9b8fa6c-47d8-5815-7c1e-23b6d5137a4b"), "{child}'s wallet is running low: {balance} left. Top up so they can buy at break.", "Push", "wallet.low_balance", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("eb05e1a5-62e5-1505-5609-da693bb3e8d9"), "A tuckshop sale of {amount} could not be settled ({reason}).", "Push", "wallet.sale_rejected", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 },
                    { new Guid("fe6f1813-f3f3-885b-7352-f0a7b9604ce7"), "{shop} today: sales {sales}, profit {profit}, {customers} customers. Low stock: {low}. Expiring soon: {expiring}. Cash-up: {cashup}. Cancellations: {cancellations}.", "Whatsapp", "daily.summary", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "en", null, null, 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_message_templates_code_language_channel",
                schema: "messaging",
                table: "message_templates",
                columns: new[] { "code", "language", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbound_messages_dedupe_key",
                schema: "messaging",
                table: "outbound_messages",
                column: "dedupe_key",
                unique: true,
                filter: "dedupe_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_messages_status_next_attempt_at",
                schema: "messaging",
                table: "outbound_messages",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "messaging",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_templates",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "outbound_messages",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "messaging");
        }
    }
}
