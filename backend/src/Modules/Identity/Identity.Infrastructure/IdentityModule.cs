using Identity.Contracts;
using Identity.Application.Devices;
using Identity.Application.Setup;
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
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, IdentityMigrator>();
        services.AddScoped<IShopStore, ShopStore>();
        services.AddScoped<ITillAccessChecker, TillAccessChecker>();
        services.AddScoped<IShopDirectory, ShopDirectory>();
        services.AddScoped<IPersonDirectory, PersonDirectory>();
        services.AddScoped<IDeviceDirectory, DeviceDirectory>();
        services.AddScoped<ISetupStore, SetupStore>();
        services.AddScoped<IMembershipReadStore, MembershipReadStore>();
        services.AddScoped<IPermissionChecker, PermissionChecker>();
        services.AddScoped<IOrganisationAccess, OrganisationAccess>();
        services.AddScoped<ISupplierOrganisations, SupplierOrganisationService>();
        services.AddScoped<IStaffStore, StaffStore>();
        services.AddScoped<IDeviceStore, DeviceStore>();
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
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new IdentityDbContext(options);
    }
}
