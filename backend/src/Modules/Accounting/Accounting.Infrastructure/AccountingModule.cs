using Accounting.Application;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Accounting.Infrastructure;

public static partial class AccountingModule
{
    public static IServiceCollection AddAccountingModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AccountingDbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", AccountingDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, AccountingMigrator>();
        services.AddDispatching(typeof(AccountingApplicationMarker).Assembly);
        AddServices(services);
        return services;
    }

    // Stores and other services of this module are registered in AccountingServices.cs.
    static partial void AddServices(IServiceCollection services);
}

internal sealed class AccountingMigrator(AccountingDbContext db) : IModuleMigrator
{
    public string Module => "Accounting";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class AccountingDesignTimeFactory : IDesignTimeDbContextFactory<AccountingDbContext>
{
    public AccountingDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", AccountingDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new AccountingDbContext(options);
    }
}
