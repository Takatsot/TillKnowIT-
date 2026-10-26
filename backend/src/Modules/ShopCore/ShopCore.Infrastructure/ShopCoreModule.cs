using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using ShopCore.Infrastructure.Persistence;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace ShopCore.Infrastructure;

public static class ShopCoreModule
{
    public static IServiceCollection AddShopCoreModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ShopCoreDbContext>(o => o
            .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", ShopCoreDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, ShopCoreMigrator>();
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
                 ?? "Host=localhost;Port=5432;Database=tillknowit;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<ShopCoreDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations", ShopCoreDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new ShopCoreDbContext(options);
    }
}
