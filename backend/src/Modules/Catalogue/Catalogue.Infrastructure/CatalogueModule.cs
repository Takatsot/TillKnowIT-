using Catalogue.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Catalogue.Infrastructure;

public static class CatalogueModule
{
    public static IServiceCollection AddCatalogueModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CatalogueDbContext>(o => o
            .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", CatalogueDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, CatalogueMigrator>();
        return services;
    }
}

internal sealed class CatalogueMigrator(CatalogueDbContext db) : IModuleMigrator
{
    public string Module => "Catalogue";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class CatalogueDesignTimeFactory : IDesignTimeDbContextFactory<CatalogueDbContext>
{
    public CatalogueDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Host=localhost;Port=5432;Database=tillknowit;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<CatalogueDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations", CatalogueDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new CatalogueDbContext(options);
    }
}
