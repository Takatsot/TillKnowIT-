using Identity.Application.Shops;
using Identity.Application.Staff;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TillKnowIT.Dispatching;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Identity.Infrastructure;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<IdentityDbContext>(o => o
            .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, IdentityMigrator>();
        services.AddScoped<IShopStore, ShopStore>();
        services.AddScoped<IStaffStore, StaffStore>();
        services.AddSingleton<IPinHasher, Pbkdf2PinHasher>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddDispatching(typeof(CreateShop).Assembly);
        return services;
    }
}

internal sealed class IdentityMigrator(IdentityDbContext db) : IModuleMigrator
{
    public string Module => "Identity";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class IdentityDesignTimeFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Host=localhost;Port=5432;Database=tillknowit;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations", IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new IdentityDbContext(options);
    }
}
