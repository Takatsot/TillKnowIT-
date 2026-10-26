using Catalogue.Infrastructure;
using Identity.Infrastructure;
using Marketplace.Infrastructure;
using Messaging.Infrastructure;
using ShopCore.Infrastructure;
using TillKnowIT.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

var db = builder.Configuration.GetConnectionString("Postgres")
         ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not set.");

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services
    .AddIdentityModule(db)
    .AddCatalogueModule(db)
    .AddShopCoreModule(db)
    .AddMarketplaceModule(db)
    .AddMessagingModule(db);

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

app.Run();

public partial class Program;
