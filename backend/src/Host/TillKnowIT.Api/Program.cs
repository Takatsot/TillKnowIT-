using Accounting.Infrastructure;
using Insights.Infrastructure;
using Finance.Infrastructure;
using Billing.Infrastructure;
using Bookings.Infrastructure;
using Customers.Infrastructure;
using Platform.Infrastructure;
using Wallet.Infrastructure;
using Payments.Infrastructure;
using Catalogue.Infrastructure;
using Identity.Infrastructure;
using Marketplace.Infrastructure;
using Messaging.Infrastructure;
using ShopCore.Infrastructure;
using TillKnowIT.SharedKernel;
using TillKnowIT.Storage;

var builder = WebApplication.CreateBuilder(args);

var db = builder.Configuration.GetConnectionString("Database")
         ?? throw new InvalidOperationException("ConnectionStrings:Database is not set.");

builder.Services.AddEncryptedLocalFileStore(builder.Configuration["Storage:Root"], builder.Configuration["Storage:Key"]);
builder.Services.AddSlipOptions(builder.Configuration["Slips:BaseUrl"], builder.Configuration["Slips:SigningKey"]);
builder.Services.AddOpenApi();
builder.Services.AddIdentityAuth(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddProblemDetails();
builder.Services
    .AddIdentityModule(db)
    .AddCatalogueModule(db)
    .AddShopCoreModule(db)
    .AddMarketplaceModule(db)
    .AddMessagingModule(db)
    .AddAccountingModule(db)
    .AddInsightsModule(db)
    .AddFinanceModule(db)
    .AddBillingModule(db)
    .AddBookingsModule(db)
    .AddCustomersModule(db)
    .AddPlatformModule(db)
    .AddWalletModule(builder.Configuration.GetConnectionString("Wallet") ?? db)
    .AddPaymentsModule(db);

// Real Xero and Sage sign-in and export, for whichever have credentials configured (Accounting:Xero:*, Accounting:Sage:*, Accounting:TokenKey, Accounting:ReturnUrl).
builder.Services.AddAccountingOAuth(AccountingOAuthExtensions.ReadOptions(k => builder.Configuration[k]));

// A fake VAS partner exists in Development only. Elsewhere airtime and electricity report "not available"
// until the real partner adapter is registered.
if (builder.Environment.IsDevelopment()) builder.Services.AddDevVasPartner().AddDevPaymentPartner().AddDevStoredValuePartner().AddDevBillingCharger().AddDevFinancePartner().AddDevAccountingExporters();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Local convenience only. Staging and production apply migrations as a deploy step (migration bundle).
    using var scope = app.Services.CreateScope();
    foreach (var migrator in scope.ServiceProvider.GetServices<IModuleMigrator>())
        await migrator.MigrateAsync();
}

app.UseExceptionHandler();
app.UseMiddleware<TillKnowIT.Api.PersonAuthMiddleware>();

var api = app.MapGroup("/api/v1");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
api.MapAuthEndpoints();
api.MapIdentityEndpoints();
api.MapShopCoreEndpoints();
api.MapTransferEndpoints();
api.MapCatalogueEndpoints();
api.MapAccountingEndpoints();
api.MapInsightsEndpoints();
api.MapFinanceEndpoints();
api.MapBillingEndpoints();
api.MapBookingsEndpoints();
api.MapCustomersEndpoints();
api.MapPlatformEndpoints();
api.MapPartnerEndpoints();
api.MapWalletEndpoints();
api.MapPaymentsEndpoints();
api.MapMarketplaceEndpoints();

app.Run();

public partial class Program;
