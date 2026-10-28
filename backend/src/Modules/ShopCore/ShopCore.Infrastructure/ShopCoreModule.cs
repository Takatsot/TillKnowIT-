using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShopCore.Application.Catalogue;
using ShopCore.Application.Compliance;
using ShopCore.Application.Imports;
using ShopCore.Application.Credit;
using ShopCore.Application.Notifications;
using ShopCore.Application.Slips;
using ShopCore.Application.Vas;
using ShopCore.Application.Insights;
using ShopCore.Application.Reports;
using ShopCore.Application.Stock;
using ShopCore.Contracts;
using ShopCore.Application.Selling;
using ShopCore.Application.Sync;
using TillKnowIT.Dispatching;
using ShopCore.Infrastructure.Persistence;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace ShopCore.Infrastructure;

public static class ShopCoreModule
{
    /// <summary>
    /// Slip links: where they point and the secret that signs them (32 random bytes in base64, from Key Vault outside development).
    /// Without a valid secret the API refuses to start, rather than sign links with a guessable key.
    /// </summary>
    public static IServiceCollection AddSlipOptions(this IServiceCollection services, string? baseUrl, string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("Slips:BaseUrl is not set.");
        if (string.IsNullOrWhiteSpace(base64Key)) throw new InvalidOperationException("Slips:SigningKey is not set.");
        byte[] key;
        try { key = Convert.FromBase64String(base64Key); }
        catch (FormatException) { throw new InvalidOperationException("Slips:SigningKey must be base64."); }
        if (key.Length != 32) throw new InvalidOperationException("Slips:SigningKey must be 32 bytes (44 base64 characters).");

        services.AddSingleton(new SlipOptions(key, baseUrl));
        return services;
    }

    public static IServiceCollection AddShopCoreModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ShopCoreDbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", ShopCoreDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, ShopCoreMigrator>();
        services.AddScoped<IUploadStore, UploadStore>();
        services.AddScoped<IRefundStore, RefundStore>();
        services.AddScoped<IRefundEffects>(sp => sp.GetRequiredService<IUploadStore>());
        services.AddScoped<ISellingStore, SellingStore>();
        services.AddScoped<IFeatureGate, FeatureGate>();
        services.AddScoped<ICreditReadStore, CreditReadStore>();
        services.AddScoped<IStockReadStore, StockReadStore>();
        services.AddScoped<IShopStockInfo, ShopStockInfo>();
        services.AddScoped<IComplianceStore, ComplianceStore>();
        services.AddScoped<ISlipStore, SlipStore>();
        services.AddScoped<IVasStore, VasStore>();
        services.TryAddSingleton(VasOptions.Default);
        services.AddScoped<IImportStore, ImportStore>();
        services.AddScoped<IProductRepointStore, ProductRepointStore>();
        services.AddScoped<INotificationReadStore, NotificationReadStore>();
        services.AddScoped<OwnerMessenger>();
        services.AddScoped<IInsightsReadStore, InsightsReadStore>();
        services.AddScoped<ITransferStore, TransferStore>();
        services.AddScoped<IReportReadStore, InsightsReadStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddDispatching(typeof(UploadBatch).Assembly);
        return services;
    }
}

internal sealed class ShopCoreMigrator(ShopCoreDbContext db) : IModuleMigrator
{
    public string Module => "ShopCore";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class ShopCoreDesignTimeFactory : IDesignTimeDbContextFactory<ShopCoreDbContext>
{
    public ShopCoreDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ShopCoreDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", ShopCoreDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new ShopCoreDbContext(options);
    }
}
