using Microsoft.EntityFrameworkCore;
using ShopCore.Application.Compliance;
using ShopCore.Domain;
using ShopCore.Infrastructure.Persistence;

namespace ShopCore.Infrastructure;

internal sealed class ComplianceStore(ShopCoreDbContext db) : IComplianceStore
{
    public async Task Add(ComplianceDocument document, CancellationToken ct) => await db.Set<ComplianceDocument>().AddAsync(document, ct);

    public Task<List<ComplianceDocument>> List(Guid shopId, CancellationToken ct) =>
        db.Set<ComplianceDocument>().AsNoTracking().Where(d => d.ShopId == shopId).ToListAsync(ct);

    public Task<ComplianceDocument?> Get(Guid shopId, Guid documentId, CancellationToken ct) =>
        db.Set<ComplianceDocument>().FirstOrDefaultAsync(d => d.Id == documentId && d.ShopId == shopId, ct);

    public void Remove(ComplianceDocument document) => db.Set<ComplianceDocument>().Remove(document);

    public Task<List<ComplianceDocument>> ExpiringBy(DateOnly date, CancellationToken ct) =>
        db.Set<ComplianceDocument>().AsNoTracking().Where(d => d.ExpiresOn != null && d.ExpiresOn <= date).ToListAsync(ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
