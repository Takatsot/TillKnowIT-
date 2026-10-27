using Marketplace.Application.Orders;
using Marketplace.Contracts;
using Marketplace.Domain;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TillKnowIT.Dispatching;

namespace Marketplace.Infrastructure;

internal sealed class GroupDealStore(MarketplaceDbContext db) : IGroupDealStore
{
    private IQueryable<GroupDeal> WithCommitments() => db.Set<GroupDeal>().Include(d => d.Commitments);

    public async Task Add(GroupDeal deal, CancellationToken ct) => await db.Set<GroupDeal>().AddAsync(deal, ct);

    public Task<GroupDeal?> Get(Guid dealId, CancellationToken ct) => WithCommitments().FirstOrDefaultAsync(d => d.Id == dealId, ct);

    public Task<List<GroupDeal>> OpenInArea(string areaCode, CancellationToken ct) =>
        WithCommitments().AsNoTracking().Where(d => d.AreaCode == areaCode && d.Status == GroupDealStatus.Open).ToListAsync(ct);

    public Task<List<GroupDeal>> ForSupplier(Guid supplierId, CancellationToken ct) =>
        WithCommitments().AsNoTracking().Where(d => d.SupplierId == supplierId).ToListAsync(ct);

    public Task<List<GroupDeal>> NeedingClosure(DateTimeOffset now, int take, CancellationToken ct) =>
        WithCommitments().Where(d =>
                (d.Status == GroupDealStatus.Open && d.ClosesAt <= now) ||
                (d.Status == GroupDealStatus.Reached && d.Commitments.Any(c => c.OrderId == null)))
            .OrderBy(d => d.ClosesAt).Take(take).ToListAsync(ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal sealed class RatingStore(MarketplaceDbContext db) : IRatingStore
{
    public Task<Rating?> ForOrder(Guid orderId, CancellationToken ct) => db.Set<Rating>().FirstOrDefaultAsync(r => r.OrderId == orderId, ct);
    public async Task Add(Rating rating, CancellationToken ct) => await db.Set<Rating>().AddAsync(rating, ct);
    public Task<List<Rating>> ForSupplier(Guid supplierId, CancellationToken ct) => db.Set<Rating>().AsNoTracking().Where(r => r.SupplierId == supplierId).ToListAsync(ct);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

/// <summary>Every minute, closes group deals whose time is up (or that the supplier closed early) and makes the shops' orders.</summary>
public sealed class GroupDealClosingService(IServiceScopeFactory scopes, ILogger<GroupDealClosingService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new CloseDueGroupDeals(), stoppingToken);
                if (result.IsSuccess) foreach (var closed in result.Value!) log.LogInformation("Group deal {Deal}: {Outcome}, {Orders} orders", closed.GroupDealId, closed.Outcome, closed.Orders);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Closing group deals failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

public static class MarketplaceHostingExtensions
{
    /// <summary>Closes group deals at their deadline. Call this from the worker only.</summary>
    public static IServiceCollection AddGroupDealClosing(this IServiceCollection services) => services.AddHostedService<GroupDealClosingService>();
}
