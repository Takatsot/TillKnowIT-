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
    .AddPaymentsModule(db);

// One publisher per module outbox.
builder.Services
    .AddOutboxProcessor<IdentityDbContext>()
    .AddOutboxProcessor<CatalogueDbContext>()
    .AddOutboxProcessor<ShopCoreDbContext>()
    .AddOutboxProcessor<MarketplaceDbContext>()
    .AddOutboxProcessor<MessagingDbContext>()
    .AddOutboxProcessor<PaymentsDbContext>();

// Delivers queued messages. Pretend providers (log only) exist in Development; elsewhere a channel with no
// real provider fails visibly rather than pretending to send.
builder.Services.AddMessageSender();
builder.Services.AddOwnerNotifications();
builder.Services.AddInvoiceReading();
builder.Services.AddPaymentRelease();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddMessagingDevProviders();
    builder.Services.AddDevInvoiceReader();
    builder.Services.AddDevPaymentPartner();
}

builder.Build().Run();
