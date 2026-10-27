using Customers.Application;
using Customers.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Customers.Infrastructure;

public static partial class CustomersModule
{
    public static IServiceCollection AddCustomersModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CustomersDbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", CustomersDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, CustomersMigrator>();
        services.AddDispatching(typeof(CustomersApplicationMarker).Assembly);
        AddServices(services);
        return services;
    }

    // Stores and other services of this module are registered in CustomersServices.cs.
    static partial void AddServices(IServiceCollection services);
}

internal sealed class CustomersMigrator(CustomersDbContext db) : IModuleMigrator
{
    public string Module => "Customers";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class CustomersDesignTimeFactory : IDesignTimeDbContextFactory<CustomersDbContext>
{
    public CustomersDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<CustomersDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", CustomersDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new CustomersDbContext(options);
    }
}
