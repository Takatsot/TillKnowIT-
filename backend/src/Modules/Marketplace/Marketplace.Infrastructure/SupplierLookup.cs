using Marketplace.Application.Suppliers;
using Marketplace.Domain;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure;

internal sealed class SupplierLookup(MarketplaceDbContext db) : ISupplierLookup
{
    public Task<Supplier?> ByOrganisation(Guid organisationId, CancellationToken ct) => db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(s => s.OrganisationId == organisationId, ct);
}
