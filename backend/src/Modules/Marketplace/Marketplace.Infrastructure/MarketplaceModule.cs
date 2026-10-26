using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Marketplace.Infrastructure;

public static class MarketplaceModule
{
    public static IServiceCollection AddMarketplaceModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<MarketplaceDbContext>(o => o
            .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", MarketplaceDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, MarketplaceMigrator>();
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
                 ?? "Host=localhost;Port=5432;Database=tillknowit;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations", MarketplaceDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new MarketplaceDbContext(options);
    }
}
