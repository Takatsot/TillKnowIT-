using Finance.Application;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Finance.Infrastructure;

public static partial class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<FinanceDbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", FinanceDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, FinanceMigrator>();
        services.AddDispatching(typeof(FinanceApplicationMarker).Assembly);
        AddServices(services);
        return services;
    }

    // Stores and other services of this module are registered in FinanceServices.cs.
    static partial void AddServices(IServiceCollection services);
}

internal sealed class FinanceMigrator(FinanceDbContext db) : IModuleMigrator
{
    public string Module => "Finance";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class FinanceDesignTimeFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", FinanceDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new FinanceDbContext(options);
    }
}
