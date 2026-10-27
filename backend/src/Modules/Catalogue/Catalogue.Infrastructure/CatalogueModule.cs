using Catalogue.Application.Admin;
using Catalogue.Application.Products;
using Catalogue.Contracts;
using TillKnowIT.Dispatching;
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
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", CatalogueDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, CatalogueMigrator>();
        services.AddScoped<ICatalogueStore, CatalogueStore>();
        services.AddScoped<ICatalogueAdminStore, CatalogueAdminStore>();
        services.AddScoped<ILookupChecker, LookupChecker>();
        services.AddScoped<ICatalogueDirectory, CatalogueDirectory>();
        services.AddDispatching(typeof(FindProductByBarcode).Assembly);
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
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<CatalogueDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", CatalogueDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new CatalogueDbContext(options);
    }
}
