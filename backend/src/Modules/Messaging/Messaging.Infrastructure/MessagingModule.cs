using Messaging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Messaging.Infrastructure;

public static class MessagingModule
{
    public static IServiceCollection AddMessagingModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<MessagingDbContext>(o => o
            .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", MessagingDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, MessagingMigrator>();
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
                 ?? "Host=localhost;Port=5432;Database=tillknowit;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations", MessagingDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new MessagingDbContext(options);
    }
}
