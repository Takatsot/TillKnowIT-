using Customers.Application;
using Customers.Contracts;
using Customers.Domain;
using Customers.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Customers.Infrastructure;

public static partial class CustomersModule
{
    static partial void AddServices(IServiceCollection services)
    {
        services.AddScoped<ICustomerStore, CustomerStore>();
        services.AddScoped<ICustomerDirectory, CustomerDirectory>();
        services.TryAddSingleton(TimeProvider.System);
    }
}

internal sealed class CustomerStore(CustomersDbContext db) : ICustomerStore
{
    public void Add(Customer customer) => db.Set<Customer>().Add(customer);
    public Task<Customer?> Get(Guid shopId, Guid customerId, CancellationToken ct) => db.Set<Customer>().FirstOrDefaultAsync(c => c.ShopId == shopId && c.Id == customerId, ct);
    public Task<Customer?> ByPhone(Guid shopId, string phone, CancellationToken ct) => db.Set<Customer>().FirstOrDefaultAsync(c => c.ShopId == shopId && c.Phone == phone && c.MergedIntoId == null, ct);

    public Task<List<Customer>> Search(Guid shopId, string text, int take, CancellationToken ct)
    {
        var phone = SaPhone.Normalise(text);
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return db.Set<Customer>().AsNoTracking()
            .Where(c => c.ShopId == shopId && c.MergedIntoId == null && (c.Name.Contains(text) || (phone != null && c.Phone == phone) || (digits.Length >= 3 && c.Phone.Contains(digits))))
            .OrderBy(c => c.Name).Take(take).ToListAsync(ct);
    }

    public void Add(LoyaltyEntry entry) => db.Set<LoyaltyEntry>().Add(entry);
    public Task<LoyaltyEntry?> EntryByReference(Guid customerId, string reference, CancellationToken ct) => db.Set<LoyaltyEntry>().AsNoTracking().FirstOrDefaultAsync(e => e.CustomerId == customerId && e.Reference == reference, ct);
    public Task<List<LoyaltyEntry>> EntriesForSale(Guid saleId, CancellationToken ct) => db.Set<LoyaltyEntry>().AsNoTracking().Where(e => e.SaleId == saleId).ToListAsync(ct);
    public async Task<int> Balance(Guid customerId, CancellationToken ct) => await db.Set<LoyaltyEntry>().Where(e => e.CustomerId == customerId).SumAsync(e => (int?)e.Points, ct) ?? 0;

    public async Task<Dictionary<Guid, int>> Balances(IReadOnlyCollection<Guid> customerIds, CancellationToken ct)
    {
        var ids = customerIds.ToList();
        return await db.Set<LoyaltyEntry>().Where(e => ids.Contains(e.CustomerId)).GroupBy(e => e.CustomerId).Select(g => new { g.Key, Sum = g.Sum(e => e.Points) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
    }

    public Task<List<LoyaltyEntry>> Recent(Guid customerId, int take, CancellationToken ct) => db.Set<LoyaltyEntry>().AsNoTracking().Where(e => e.CustomerId == customerId).OrderByDescending(e => e.CreatedAt).Take(take).ToListAsync(ct);

    public async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            // The same phone number added at the same moment on two tills: the second is the same customer, and the till asks again.
            throw new TillKnowIT.SharedKernel.ConcurrencyConflictException("The same record was added at the same moment.", ex);
        }
    }
}
