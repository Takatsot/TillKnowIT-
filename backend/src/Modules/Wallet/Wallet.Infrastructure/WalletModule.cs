using Wallet.Application;
using Wallet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Wallet.Infrastructure;

public static partial class WalletModule
{
    public static IServiceCollection AddWalletModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<WalletDbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", WalletDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, WalletMigrator>();
        services.AddDispatching(typeof(WalletApplicationMarker).Assembly);
        AddServices(services);
        return services;
    }

    // Stores and other services of this module are registered in WalletServices.cs.
    static partial void AddServices(IServiceCollection services);
}

internal sealed class WalletMigrator(WalletDbContext db) : IModuleMigrator
{
    public string Module => "Wallet";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class WalletDesignTimeFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", WalletDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new WalletDbContext(options);
    }
}
