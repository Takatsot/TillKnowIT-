using Bookings.Application;
using Bookings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using TillKnowIT.Dispatching;
using TillKnowIT.Outbox;
using TillKnowIT.SharedKernel;

namespace Bookings.Infrastructure;

public static partial class BookingsModule
{
    public static IServiceCollection AddBookingsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BookingsDbContext>(o => o
            .UseSqlServer(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", BookingsDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new OutboxInterceptor()));
        services.AddScoped<IModuleMigrator, BookingsMigrator>();
        services.AddDispatching(typeof(BookingsApplicationMarker).Assembly);
        AddServices(services);
        return services;
    }

    // Stores and other services of this module are registered in BookingsServices.cs.
    static partial void AddServices(IServiceCollection services);
}

internal sealed class BookingsMigrator(BookingsDbContext db) : IModuleMigrator
{
    public string Module => "Bookings";
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);
}

/// <summary>Lets `dotnet ef` build the context without running the host.</summary>
internal sealed class BookingsDesignTimeFactory : IDesignTimeDbContextFactory<BookingsDbContext>
{
    public BookingsDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("TILLKNOWIT_DB")
                 ?? "Server=localhost,1433;Database=tillknowit;User Id=sa;Password=Tillknowit_Dev1!;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<BookingsDbContext>()
            .UseSqlServer(cs, n => n.MigrationsHistoryTable("__ef_migrations", BookingsDbContext.Schema))
            .UseSnakeCaseNamingConvention().Options;
        return new BookingsDbContext(options);
    }
}
