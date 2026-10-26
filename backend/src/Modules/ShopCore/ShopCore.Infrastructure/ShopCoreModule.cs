using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShopCore.Application.Sync;
using TillKnowIT.Dispatching;
using ShopCore.Infrastructure.Persistence;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace ShopCore.Infrastructure;

public static class ShopCoreModule
{
    public static IServiceCollection AddShopCoreModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ShopCoreDbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", ShopCoreDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, ShopCoreMigrator>();
        services.AddScoped<IUploadStore, UploadStore>();
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
