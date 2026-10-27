using Messaging.Application;
using Messaging.Application.Sending;
using Messaging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Messaging.Infrastructure;

public static class MessagingModule
{
    public static IServiceCollection AddMessagingModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<MessagingDbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", MessagingDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, MessagingMigrator>();
        services.AddScoped<IMessagingStore, MessagingStore>();
        services.AddMessagingApplication();
        services.AddDispatching(typeof(SendDueMessages).Assembly);
        return services;
    }
}

internal sealed class MessagingMigrator(MessagingDbContext db) : IModuleMigrator
{
    public string Module => "Messaging";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class MessagingDesignTimeFactory : IDesignTimeDbContextFactory<MessagingDbContext>
{
    public MessagingDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", MessagingDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new MessagingDbContext(options);
    }
}
