using Payments.Application;
using Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Payments.Infrastructure;

public static partial class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PaymentsDbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", PaymentsDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, PaymentsMigrator>();
        services.AddDispatching(typeof(PaymentsApplicationMarker).Assembly);
        AddServices(services);
        return services;
    }

    // Stores and other services of this module are registered in PaymentsServices.cs.
    static partial void AddServices(IServiceCollection services);
}

internal sealed class PaymentsMigrator(PaymentsDbContext db) : IModuleMigrator
{
    public string Module => "Payments";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class PaymentsDesignTimeFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", PaymentsDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new PaymentsDbContext(options);
    }
}
