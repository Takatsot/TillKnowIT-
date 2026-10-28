using Marketplace.Application.Offers;
using Marketplace.Application.Orders;
using Marketplace.Application.Suppliers;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Marketplace.Infrastructure;

public static class MarketplaceModule
{
    public static IServiceCollection AddMarketplaceModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<MarketplaceDbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", MarketplaceDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, MarketplaceMigrator>();
        services.AddScoped<ISupplierStore, SupplierStore>();
        services.AddScoped<Marketplace.Contracts.IOrderPayments, Marketplace.Application.Orders.OrderPaymentsService>();
        services.AddScoped<ISupplierProductStore, SupplierProductStore>();
        services.AddScoped<IDealStore, DealStore>();
        services.AddScoped<IOfferStore, OfferStore>();
        services.AddScoped<IShopMarketplaceStore, ShopMarketplaceStore>();
        services.AddScoped<IOrderStore, OrderStore>();
        services.AddScoped<IGroupDealStore, GroupDealStore>();
        services.AddScoped<IBuyingGroupStore, BuyingGroupStore>();
        services.AddScoped<IRatingStore, RatingStore>();
        services.AddScoped<GroupDealClosing>();
        services.AddScoped<OrderNotifier>();
        services.AddDispatching(typeof(ApplyAsSupplier).Assembly);
        return services;
    }
}

internal sealed class MarketplaceMigrator(MarketplaceDbContext db) : IModuleMigrator
{
    public string Module => "Marketplace";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class MarketplaceDesignTimeFactory : IDesignTimeDbContextFactory<MarketplaceDbContext>
{
    public MarketplaceDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", MarketplaceDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new MarketplaceDbContext(options);
    }
}
