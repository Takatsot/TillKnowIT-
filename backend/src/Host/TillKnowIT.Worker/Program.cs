using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Insights.Infrastructure;
using Insights.Infrastructure.Persistence;
using Finance.Infrastructure;
using Finance.Infrastructure.Persistence;
using Billing.Infrastructure;
using Billing.Infrastructure.Persistence;
using Bookings.Infrastructure;
using Bookings.Infrastructure.Persistence;
using Customers.Infrastructure;
using Customers.Infrastructure.Persistence;
using Platform.Infrastructure;
using Platform.Infrastructure.Persistence;
using Wallet.Infrastructure;
using Wallet.Infrastructure.Persistence;
using Payments.Infrastructure;
using Payments.Infrastructure.Persistence;
using Catalogue.Infrastructure;
using Catalogue.Infrastructure.Persistence;
using Identity.Infrastructure;
using Identity.Infrastructure.Persistence;
using Marketplace.Infrastructure;
using Marketplace.Infrastructure.Persistence;
using Messaging.Infrastructure;
using Messaging.Infrastructure.Persistence;
using ShopCore.Infrastructure;
using ShopCore.Infrastructure.Persistence;
using TillKnowIT.Outbox;
using TillKnowIT.Storage;

var builder = Host.CreateApplicationBuilder(args);

var db = builder.Configuration.GetConnectionString("Database")
         ?? throw new InvalidOperationException("ConnectionStrings:Database is not set.");

builder.Services.AddEncryptedLocalFileStore(builder.Configuration["Storage:Root"], builder.Configuration["Storage:Key"]);

// Same modules as the API, so event handlers in any module can run in the worker.
builder.Services
    .AddIdentityModule(db)
    .AddCatalogueModule(db)
    .AddShopCoreModule(db)
    .AddMarketplaceModule(db)
    .AddMessagingModule(db)
    .AddPaymentsModule(db)
    .AddWalletModule(builder.Configuration.GetConnectionString("Wallet") ?? db);
// One publisher per module outbox.
builder.Services
    .AddOutboxProcessor<IdentityDbContext>()
    .AddOutboxProcessor<CatalogueDbContext>()
    .AddOutboxProcessor<ShopCoreDbContext>()
    .AddOutboxProcessor<MarketplaceDbContext>()
    .AddOutboxProcessor<MessagingDbContext>()
    .AddOutboxProcessor<PaymentsDbContext>()
    .AddOutboxProcessor<WalletDbContext>()
    .AddOutboxProcessor<PlatformDbContext>()
    .AddOutboxProcessor<CustomersDbContext>()
    .AddOutboxProcessor<BookingsDbContext>()
    .AddOutboxProcessor<BillingDbContext>()
    .AddOutboxProcessor<FinanceDbContext>()
    .AddOutboxProcessor<InsightsDbContext>()
    .AddOutboxProcessor<AccountingDbContext>();

// Delivers queued messages. Pretend providers (log only) exist in Development; elsewhere a channel with no
// real provider fails visibly rather than pretending to send.
builder.Services.AddMessageSender();
// A real SMS provider is used when its credentials are configured (Sms:BulkSms:TokenId and Sms:BulkSms:TokenSecret, from the secrets store in production).
builder.Services.AddBulkSmsIfConfigured(builder.Configuration["Sms:BulkSms:TokenId"], builder.Configuration["Sms:BulkSms:TokenSecret"], builder.Configuration["Sms:BulkSms:BaseUrl"], builder.Configuration["Sms:BulkSms:RoutingGroup"]);
builder.Services.AddOwnerNotifications();
builder.Services.AddInvoiceReading();
builder.Services.AddPaymentRelease();
builder.Services.AddGroupDealClosing();
builder.Services.AddLaybyExpiry();
builder.Services.AddAppointmentReminders();
builder.Services.AddWalletJobs();
builder.Services.AddBillingJobs();
builder.Services.AddAccountingExports();
builder.Services.AddAccountingOAuth(AccountingOAuthExtensions.ReadOptions(k => builder.Configuration[k]));
builder.Services.AddInsightAggregation();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddMessagingDevProviders();
    builder.Services.AddDevInvoiceReader();
    builder.Services.AddDevPaymentPartner();
    builder.Services.AddDevStoredValuePartner();
    builder.Services.AddDevBillingCharger();
    builder.Services.AddDevFinancePartner();
    builder.Services.AddDevAccountingExporters();
}

builder.Build().Run();
