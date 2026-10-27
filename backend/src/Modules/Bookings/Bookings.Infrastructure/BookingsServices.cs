using Bookings.Application;
using Bookings.Domain;
using Bookings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TillKnowIT.Dispatching;

namespace Bookings.Infrastructure;

public static partial class BookingsModule
{
    static partial void AddServices(IServiceCollection services)
    {
        services.AddScoped<IBookingStore, BookingStore>();
        services.TryAddSingleton(TimeProvider.System);
    }

    /// <summary>Sends tomorrow's appointment reminders every 15 minutes. Call this from the worker only.</summary>
    public static IServiceCollection AddAppointmentReminders(this IServiceCollection services) => services.AddHostedService<ReminderService>();
}

internal sealed class BookingStore(BookingsDbContext db) : IBookingStore
{
    public void Add(ServiceItem service) => db.Set<ServiceItem>().Add(service);
    public Task<ServiceItem?> GetService(Guid shopId, Guid serviceId, CancellationToken ct) => db.Set<ServiceItem>().FirstOrDefaultAsync(s => s.ShopId == shopId && s.Id == serviceId, ct);
    public Task<List<ServiceItem>> Services(Guid shopId, CancellationToken ct) => db.Set<ServiceItem>().AsNoTracking().Where(s => s.ShopId == shopId).ToListAsync(ct);
    public Task<List<StaffService>> StaffServices(Guid shopId, CancellationToken ct) => db.Set<StaffService>().AsNoTracking().Where(s => s.ShopId == shopId).ToListAsync(ct);

    public async Task ReplaceStaff(Guid shopId, Guid serviceId, IReadOnlyList<Guid> staffIds, DateTimeOffset now, CancellationToken ct)
    {
        db.Set<StaffService>().RemoveRange(await db.Set<StaffService>().Where(s => s.ShopId == shopId && s.ServiceId == serviceId).ToListAsync(ct));
        foreach (var id in staffIds) db.Set<StaffService>().Add(new StaffService(shopId, id, serviceId, now));
    }

    public void Add(Appointment appointment) => db.Set<Appointment>().Add(appointment);
    public Task<Appointment?> GetAppointment(Guid shopId, Guid appointmentId, CancellationToken ct) => db.Set<Appointment>().FirstOrDefaultAsync(a => a.ShopId == shopId && a.Id == appointmentId, ct);

    public Task<List<Appointment>> Between(Guid shopId, DateTimeOffset from, DateTimeOffset to, Guid? staffId, CancellationToken ct) =>
        db.Set<Appointment>().Where(a => a.ShopId == shopId && a.StartsAt < to && a.EndsAt > from && (staffId == null || a.StaffId == staffId)).ToListAsync(ct);

    public Task<List<Appointment>> StartingBetween(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        db.Set<Appointment>().Where(a => a.StartsAt >= from && a.StartsAt < to && a.ReminderSentAt == null && a.CustomerId != null
                                         && (a.Status == AppointmentStatus.Booked || a.Status == AppointmentStatus.Confirmed)).Take(500).ToListAsync(ct);

    public void Add(CommissionRule rule) => db.Set<CommissionRule>().Add(rule);
    public Task<CommissionRule?> GetRule(Guid shopId, Guid? staffId, Guid? serviceId, CancellationToken ct) => db.Set<CommissionRule>().FirstOrDefaultAsync(r => r.ShopId == shopId && r.StaffId == staffId && r.ServiceId == serviceId, ct);
    public Task<List<CommissionRule>> Rules(Guid shopId, CancellationToken ct) => db.Set<CommissionRule>().AsNoTracking().Where(r => r.ShopId == shopId).ToListAsync(ct);

    public Task<List<Appointment>> CompletedBetween(Guid shopId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        db.Set<Appointment>().AsNoTracking().Where(a => a.ShopId == shopId && a.Status == AppointmentStatus.Completed && a.CompletedAt >= from && a.CompletedAt < to).ToListAsync(ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class ReminderService(IServiceScopeFactory scopes, ILogger<ReminderService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new SendAppointmentReminders(), stoppingToken);
                if (result is { IsSuccess: true, Value: > 0 }) log.LogInformation("Sent {Count} appointment reminders", result.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Sending appointment reminders failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
