using Insights.Application;
using Insights.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Insights.Infrastructure;

public static partial class InsightsModule
{
    public static IServiceCollection AddInsightsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<InsightsDbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", InsightsDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, InsightsMigrator>();
        services.AddDispatching(typeof(InsightsApplicationMarker).Assembly);
        AddServices(services);
        return services;
    }

    // Stores and other services of this module are registered in InsightsServices.cs.
    static partial void AddServices(IServiceCollection services);
}

internal sealed class InsightsMigrator(InsightsDbContext db) : IModuleMigrator
{
    public string Module => "Insights";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class InsightsDesignTimeFactory : IDesignTimeDbContextFactory<InsightsDbContext>
{
    public InsightsDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<InsightsDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", InsightsDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new InsightsDbContext(options);
    }
}
