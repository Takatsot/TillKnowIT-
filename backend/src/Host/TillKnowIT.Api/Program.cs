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
builder.Services.AddProblemDetails();
builder.Services
    .AddIdentityModule(db)
    .AddCatalogueModule(db)
    .AddShopCoreModule(db)
    .AddMarketplaceModule(db)
    .AddMessagingModule(db)
    .AddWalletModule(builder.Configuration.GetConnectionString("Wallet") ?? db)
    .AddPaymentsModule(db);

// A fake VAS partner exists in Development only. Elsewhere airtime and electricity report "not available"
// until the real partner adapter is registered.
if (builder.Environment.IsDevelopment()) builder.Services.AddDevVasPartner().AddDevPaymentPartner().AddDevStoredValuePartner();

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

var api = app.MapGroup("/api/v1");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
api.MapIdentityEndpoints();
api.MapShopCoreEndpoints();
api.MapCatalogueEndpoints();
api.MapWalletEndpoints();
api.MapPaymentsEndpoints();
api.MapMarketplaceEndpoints();

app.Run();

public partial class Program;
