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
// Demo:Enabled is for a public demo with made-up data only: the demo people and shop are seeded, the fake partners stand in, and the sign-in code is shown on screen
// (there is no SMS). It still needs a real Auth:TokenKey. Never switch it on where real shops' data lives.
var demo = builder.Configuration.GetValue("Demo:Enabled", false);
builder.Services.AddIdentityAuth(builder.Configuration, builder.Environment.IsDevelopment(), demo);

// Which web addresses may call the API from a browser (the apps hosted elsewhere, such as GitHub Pages): Cors:Origins:0, Cors:Origins:1 ...
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (corsOrigins.Length > 0) p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Content-Disposition");
}));
builder.Services.AddExceptionHandler<BadRequestHandler>();
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
if (builder.Environment.IsDevelopment() || demo) builder.Services.AddDevVasPartner().AddDevPaymentPartner().AddDevStoredValuePartner().AddDevBillingCharger().AddDevFinancePartner().AddDevAccountingExporters();

var app = builder.Build();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

if (app.Environment.IsDevelopment() || demo)
{
    // Local convenience (and the demo) only. Staging and production apply migrations as a deploy step (migration bundle).
    using var scope = app.Services.CreateScope();
    foreach (var migrator in scope.ServiceProvider.GetServices<IModuleMigrator>())
        await migrator.MigrateAsync();

    // Development only: one person for every role, a shop, a till, a supplier and a school, so every app can be signed in to (see dev-seed.md).
    if (app.Configuration.GetValue("DevSeed:Enabled", true))
        await DevSeed.Run(app.Services, app.Environment.ContentRootPath, app.Logger);
}

app.UseExceptionHandler();
app.UseCors();
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
