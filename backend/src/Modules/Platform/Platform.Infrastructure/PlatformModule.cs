using Platform.Application;
using Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Platform.Infrastructure;

public static partial class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PlatformDbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", PlatformDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, PlatformMigrator>();
        services.AddDispatching(typeof(PlatformApplicationMarker).Assembly);
        AddServices(services);
        return services;
    }

    // Stores and other services of this module are registered in PlatformServices.cs.
    static partial void AddServices(IServiceCollection services);
}

internal sealed class PlatformMigrator(PlatformDbContext db) : IModuleMigrator
{
    public string Module => "Platform";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class PlatformDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", PlatformDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new PlatformDbContext(options);
    }
}
