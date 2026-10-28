using Billing.Application;
using Billing.Contracts;
using Billing.Domain;
using Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Billing.Infrastructure;

public static partial class BillingModule
{
    static partial void AddServices(IServiceCollection services)
    {
        services.AddScoped<IBillingStore, BillingStore>();
        services.AddScoped<Invoicing>();
        services.AddScoped<IEntitlements, Entitlements>();
        services.AddScoped<IUsageMeter, UsageMeter>();
        services.AddScoped<IMessageAllowance, MessageAllowanceService>();
        services.AddScoped<ISponsoredPlacements, SponsoredPlacementService>();
        services.TryAddSingleton(TimeProvider.System);
        // Plans carry the prices; these are the other numbers (VAT, grace period, reminder days, price of a message above the plan) and come from settings.
        services.TryAddSingleton(sp => sp.GetService<IConfiguration>()?.GetSection("Billing").Get<BillingSettings>() ?? new BillingSettings());
    }

    /// <summary>Renews subscriptions and chases failed payments every hour. Call this from the worker only.</summary>
    public static IServiceCollection AddBillingJobs(this IServiceCollection services) => services.AddHostedService<BillingJobsService>();

    /// <summary>Development only: charges always succeed, so nobody is billed for real. Elsewhere a charger must be provided by the payment gateway.</summary>
    public static IServiceCollection AddDevBillingCharger(this IServiceCollection services)
    {
        services.TryAddSingleton<IBillingCharger, DevBillingCharger>();
        return services;
    }
}

/// <summary>A pretend gateway for development. It accepts every charge except those for an invoice reference that contains "fail", so failed payments can be tried by hand.</summary>
public sealed class DevBillingCharger : IBillingCharger
{
    public Task<Result> Charge(Guid organisationId, string invoiceNumber, long totalCents, string reference, CancellationToken ct) =>
        Task.FromResult(reference.Contains("fail", StringComparison.OrdinalIgnoreCase) ? Result.Fail("payment.declined", "The payment was declined.") : Result.Ok());
}

internal sealed class BillingStore(BillingDbContext db) : IBillingStore
{
    public void Add(Plan plan) => db.Set<Plan>().Add(plan);
    public Task<Plan?> PlanByCode(string code, CancellationToken ct) => db.Set<Plan>().FirstOrDefaultAsync(p => p.Code == code, ct);
    public Task<Plan?> PlanById(Guid id, CancellationToken ct) => db.Set<Plan>().FirstOrDefaultAsync(p => p.Id == id, ct);
    public Task<List<Plan>> Plans(string? audience, bool activeOnly, CancellationToken ct) =>
        db.Set<Plan>().AsNoTracking().Where(p => (audience == null || p.Audience == audience) && (!activeOnly || p.IsActive)).ToListAsync(ct);
    public Task<Plan?> FreePlan(string audience, CancellationToken ct) => db.Set<Plan>().AsNoTracking().FirstOrDefaultAsync(p => p.Audience == audience && p.PriceCents == 0 && p.IsActive, ct);

    public async Task<Dictionary<Guid, List<PlanFeature>>> FeaturesOf(IReadOnlyCollection<Guid> planIds, CancellationToken ct) =>
        (await db.Set<PlanFeature>().AsNoTracking().Where(f => planIds.Contains(f.PlanId)).ToListAsync(ct)).GroupBy(f => f.PlanId).ToDictionary(g => g.Key, g => g.ToList());

    public async Task ReplaceFeatures(Guid planId, IReadOnlyList<PlanFeatureDto> features, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.Set<PlanFeature>().Where(f => f.PlanId == planId).ToListAsync(ct);
        db.Set<PlanFeature>().RemoveRange(existing);
        await db.SaveChangesAsync(ct);   // remove first so a feature kept with a new limit does not meet its old row in the unique index
        foreach (var f in features) db.Set<PlanFeature>().Add(new PlanFeature(planId, f.Feature, f.Limit, now));
    }

    public void Add(Subscription subscription) => db.Set<Subscription>().Add(subscription);
    public Task<Subscription?> SubscriptionOf(Guid organisationId, CancellationToken ct) => db.Set<Subscription>().FirstOrDefaultAsync(s => s.OrganisationId == organisationId, ct);
    public Task<Subscription?> SubscriptionById(Guid id, CancellationToken ct) => db.Set<Subscription>().FirstOrDefaultAsync(s => s.Id == id, ct);
    public Task<List<Subscription>> DueSubscriptions(DateTimeOffset now, int take, CancellationToken ct) =>
        db.Set<Subscription>().Where(s => s.Status != SubscriptionStatus.Cancelled && s.PeriodEnd <= now).OrderBy(s => s.PeriodEnd).Take(take).ToListAsync(ct);
    public Task<List<Subscription>> PastDue(int take, CancellationToken ct) => db.Set<Subscription>().Where(s => s.Status == SubscriptionStatus.PastDue).OrderBy(s => s.PastDueSince).Take(take).ToListAsync(ct);

    public void Add(Invoice invoice, IReadOnlyList<InvoiceLine> lines)
    {
        db.Set<Invoice>().Add(invoice);
        db.Set<InvoiceLine>().AddRange(lines);
    }

    public Task<Invoice?> InvoiceByReference(string reference, CancellationToken ct) => db.Set<Invoice>().FirstOrDefaultAsync(i => i.Reference == reference, ct);
    public Task<Invoice?> InvoiceById(Guid id, CancellationToken ct) => db.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == id, ct);
    public Task<List<Invoice>> InvoicesOf(Guid organisationId, int take, CancellationToken ct) => db.Set<Invoice>().AsNoTracking().Where(i => i.OrganisationId == organisationId).OrderByDescending(i => i.CreatedAt).Take(take).ToListAsync(ct);
    public Task<Invoice?> LatestFailed(Guid organisationId, CancellationToken ct) => db.Set<Invoice>().Where(i => i.OrganisationId == organisationId && i.Status == InvoiceStatus.Failed).OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync(ct);
    public Task<List<InvoiceLine>> LinesOf(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct) => db.Set<InvoiceLine>().AsNoTracking().Where(l => invoiceIds.Contains(l.InvoiceId)).OrderBy(l => l.CreatedAt).ToListAsync(ct);
    public Task<int> InvoiceCount(CancellationToken ct) => db.Set<Invoice>().CountAsync(ct);

    public void Add(UsageRecord usage) => db.Set<UsageRecord>().Add(usage);
    public Task<UsageRecord?> UsageByReference(Guid organisationId, string reference, CancellationToken ct) => db.Set<UsageRecord>().AsNoTracking().FirstOrDefaultAsync(u => u.OrganisationId == organisationId && u.Reference == reference, ct);
    public async Task<long> UsageTotal(Guid organisationId, string metric, DateTimeOffset from, CancellationToken ct) =>
        await db.Set<UsageRecord>().AsNoTracking().Where(u => u.OrganisationId == organisationId && u.Metric == metric && u.OccurredAt >= from).SumAsync(u => (long?)u.Quantity, ct) ?? 0;
    public Task<List<UsageRecord>> UnbilledUsage(Guid organisationId, DateTimeOffset before, CancellationToken ct) => db.Set<UsageRecord>().Where(u => u.OrganisationId == organisationId && u.InvoiceId == null && u.OccurredAt < before).ToListAsync(ct);
    public Task<List<Guid>> OrganisationsWithUnbilledPricedUsage(DateTimeOffset before, int take, CancellationToken ct) =>
        db.Set<UsageRecord>().Where(u => u.InvoiceId == null && u.UnitCents != null && u.OccurredAt < before).Select(u => u.OrganisationId).Distinct().Take(take).ToListAsync(ct);

    public void Add(SponsoredCampaign campaign) => db.Set<SponsoredCampaign>().Add(campaign);
    public Task<SponsoredCampaign?> Campaign(Guid id, CancellationToken ct) => db.Set<SponsoredCampaign>().FirstOrDefaultAsync(c => c.Id == id, ct);
    public Task<List<SponsoredCampaign>> CampaignsOf(Guid supplierOrganisationId, CancellationToken ct) => db.Set<SponsoredCampaign>().AsNoTracking().Where(c => c.SupplierOrganisationId == supplierOrganisationId).ToListAsync(ct);
    public Task<List<SponsoredCampaign>> RunningOn(DateOnly day, CancellationToken ct) => db.Set<SponsoredCampaign>().AsNoTracking().Where(c => c.Status == CampaignStatus.Active && c.StartsOn <= day && c.EndsOn >= day).ToListAsync(ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class BillingJobsService(IServiceScopeFactory scopes, ILogger<BillingJobsService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
                var invoices = await dispatcher.Send(new RunBilling(), stoppingToken);
                var reminders = await dispatcher.Send(new RunDunning(), stoppingToken);
                if (invoices.Value > 0 || reminders.Value > 0) log.LogInformation("Billing run: {Invoices} invoices, {Reminders} reminders", invoices.Value, reminders.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Billing run failed");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
