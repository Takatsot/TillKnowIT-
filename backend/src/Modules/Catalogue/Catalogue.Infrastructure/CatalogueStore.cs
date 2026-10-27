using Catalogue.Application.Products;
using Catalogue.Contracts;
using Catalogue.Domain;
using Catalogue.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Catalogue.Infrastructure;

internal sealed class CatalogueStore(CatalogueDbContext db) : ICatalogueStore
{
    public Task<CatalogueProduct?> FindByBarcode(string barcode, CancellationToken ct) =>
        db.Products.FirstOrDefaultAsync(p => p.Barcode == barcode, ct);

    public async Task Add(CatalogueProduct product, CancellationToken ct) => await db.Products.AddAsync(product, ct);

    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}

internal sealed class LookupChecker(CatalogueDbContext db) : ILookupChecker
{
    public Task<bool> IsActive(string type, string code, CancellationToken ct) =>
        db.Set<Lookup>().AsNoTracking().AnyAsync(l => l.Type == type && l.Code == code && l.IsActive, ct);
}

internal sealed class CatalogueDirectory(CatalogueDbContext db) : ICatalogueDirectory
{
    public async Task<Dictionary<string, CatalogueMatch>> FindApproved(IEnumerable<string> barcodes, CancellationToken ct)
    {
        var list = barcodes.Distinct().ToList();
        if (list.Count == 0) return [];
        var rows = await db.Products.AsNoTracking()
            .Where(p => p.Status == CatalogueProductStatus.Approved && list.Contains(p.Barcode))
            .Select(p => new { p.Id, p.Name, p.Barcode }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Barcode, r => new CatalogueMatch(r.Id, r.Name, r.Barcode));
    }
}
