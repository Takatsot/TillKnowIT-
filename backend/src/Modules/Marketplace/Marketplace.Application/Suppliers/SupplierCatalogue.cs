using Catalogue.Contracts;
using Identity.Contracts;
using Marketplace.Contracts;
using Marketplace.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.Importing;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Suppliers;

public interface ISupplierProductStore
{
    /// <summary>The supplier's products that have a barcode, by barcode.</summary>
    Task<Dictionary<string, SupplierProduct>> ByBarcode(Guid supplierId, CancellationToken ct);

    Task<SupplierProduct?> Get(Guid productId, CancellationToken ct);
    Task<List<SupplierProduct>> List(Guid supplierId, CancellationToken ct);
    Task Add(SupplierProduct product, CancellationToken ct);
    Task Save(CancellationToken ct);
}

/// <summary>
/// M2-1: a supplier uploads their price list (Excel or CSV). With Apply false it is a preview and nothing is saved.
/// With Apply true every usable row is saved; rows with errors are skipped and reported. Mapping overrides the guessed columns.
/// </summary>
public sealed record ImportSupplierCatalogue(
    Guid SupplierId, Guid CallerPersonId, string FileName, byte[] Content, Dictionary<string, string>? Mapping, bool Apply)
    : ICommand<CatalogueUploadResponse>;

internal sealed class ImportSupplierCatalogueHandler(
    ISupplierStore suppliers, ISupplierProductStore products, IOrganisationAccess access, ICatalogueDirectory catalogue, TimeProvider clock)
    : ICommandHandler<ImportSupplierCatalogue, CatalogueUploadResponse>
{
    public async Task<Result<CatalogueUploadResponse>> Handle(ImportSupplierCatalogue c, CancellationToken ct)
    {
        var supplier = await suppliers.Get(c.SupplierId, ct);
        if (supplier is null) return Result<CatalogueUploadResponse>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, c.CallerPersonId, "supplier.catalogue", ct);
        if (!allowed.IsSuccess) return Result<CatalogueUploadResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (supplier.Status is SupplierStatus.Rejected or SupplierStatus.Suspended or SupplierStatus.Unlisted)
            return Result<CatalogueUploadResponse>.Fail("supplier.cannot_upload", "This supplier cannot upload a catalogue.");

        var read = SheetReader.Read(c.FileName, c.Content);
        if (!read.IsSuccess) return Result<CatalogueUploadResponse>.Fail(read.Error!.Code, read.Error.Message);
        var sheet = read.Value!;

        ColumnMapping mapping;
        if (c.Mapping is { Count: > 0 })
        {
            var headers = sheet.Headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (c.Mapping.Keys.Any(k => !ColumnMapper.Fields.Contains(k)) || c.Mapping.Values.Any(h => !headers.Contains(h)))
                return Result<CatalogueUploadResponse>.Fail("import.mapping_invalid", "A chosen field or column is not valid for this file.");
            mapping = new ColumnMapping(c.Mapping);
        }
        else mapping = ColumnMapper.Suggest(sheet.Headers);

        var raw = sheet.Rows.Select((row, i) =>
        {
            string? Cell(string field) => ColumnMapper.Cell(sheet, row, mapping, field);
            return new RawCatalogueRow(i + 2, Cell(ColumnMapper.Barcode), Cell(ColumnMapper.Name), ColumnMapper.ParseWholeNumber(Cell(ColumnMapper.UnitsPerCase)),
                ColumnMapper.ParseMoneyCents(Cell(ColumnMapper.CaseCost)), ColumnMapper.ParseMoneyCents(Cell(ColumnMapper.UnitCost)),
                ColumnMapper.ParseFlag(Cell(ColumnMapper.VatIncluded)), ColumnMapper.ParseFlag(Cell(ColumnMapper.InStock)));
        }).ToList();

        var existing = await products.ByBarcode(supplier.Id, ct);
        var matches = (await catalogue.FindApproved(raw.Where(r => !string.IsNullOrWhiteSpace(r.Barcode)).Select(r => r.Barcode!.Trim()), ct))
            .ToDictionary(kv => kv.Key, kv => new SupplierCatalogue.CatalogueMatch(kv.Value.Id, kv.Value.Name));
        var plan = SupplierCatalogue.Plan(raw, existing, matches);

        if (c.Apply)
        {
            var now = clock.GetUtcNow();
            foreach (var line in plan)
            {
                var input = new SupplierProductInput(line.Barcode, line.Name, line.UnitsPerCase, line.CasePriceCents, line.VatIncluded, line.InStock, null);
                if (line.Action == "create")
                {
                    var created = SupplierProduct.Create(supplier.Id, line.CatalogueProductId, input, now);
                    if (created.IsSuccess) await products.Add(created.Value!, ct);
                }
                else if (line.Action == "update" && line.Barcode is not null && existing.TryGetValue(line.Barcode, out var product))
                {
                    product.Update(input, line.CatalogueProductId);
                }
            }
            await products.Save(ct);
        }

        int Count(string action) => plan.Count(l => l.Action == action);
        return Result<CatalogueUploadResponse>.Ok(new CatalogueUploadResponse(
            c.Apply, Count("create"), Count("update"), Count("unchanged"), Count("error"),
            plan.Select(l => new CatalogueLineResponse(l.Row, l.Barcode, l.Name, l.UnitsPerCase, l.CasePriceCents, l.VatIncluded, l.InStock, l.CatalogueProductId, l.Action, l.Issues)).ToList(),
            [.. sheet.Headers], mapping.FieldToHeader));
    }
}

public sealed record ListSupplierProducts(Guid SupplierId, Guid CallerPersonId) : IQuery<List<SupplierProductResponse>>;

internal sealed class ListSupplierProductsHandler(ISupplierStore suppliers, ISupplierProductStore products, IOrganisationAccess access)
    : IQueryHandler<ListSupplierProducts, List<SupplierProductResponse>>
{
    public async Task<Result<List<SupplierProductResponse>>> Handle(ListSupplierProducts q, CancellationToken ct)
    {
        var supplier = await suppliers.Get(q.SupplierId, ct);
        if (supplier is null) return Result<List<SupplierProductResponse>>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, q.CallerPersonId, "supplier.catalogue", ct);
        if (!allowed.IsSuccess) return Result<List<SupplierProductResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return Result<List<SupplierProductResponse>>.Ok((await products.List(supplier.Id, ct)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Select(ProductMapping.ToResponse).ToList());
    }
}

internal static class ProductMapping
{
    public static SupplierProductResponse ToResponse(SupplierProduct p) =>
        new(p.Id, p.Barcode, p.Name, p.UnitsPerCase, p.CasePriceCents, p.UnitPriceCents, p.VatIncluded, p.InStock, p.ExpiryPolicy, p.CatalogueProductId);
}

public sealed record UpdateSupplierProduct(Guid ProductId, UpdateSupplierProductRequest Request) : ICommand<SupplierProductResponse>;

internal sealed class UpdateSupplierProductHandler(ISupplierStore suppliers, ISupplierProductStore products, IOrganisationAccess access)
    : ICommandHandler<UpdateSupplierProduct, SupplierProductResponse>
{
    public async Task<Result<SupplierProductResponse>> Handle(UpdateSupplierProduct c, CancellationToken ct)
    {
        var product = await products.Get(c.ProductId, ct);
        if (product is null) return Result<SupplierProductResponse>.Fail("supplier_product.not_found", "Product not found.");
        var supplier = await suppliers.Get(product.SupplierId, ct);
        if (supplier is null) return Result<SupplierProductResponse>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, c.Request.CallerPersonId, "supplier.catalogue", ct);
        if (!allowed.IsSuccess) return Result<SupplierProductResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var r = c.Request;
        var input = new SupplierProductInput(product.Barcode, r.Name ?? product.Name, r.UnitsPerCase ?? product.UnitsPerCase,
            r.CasePriceCents ?? product.CasePriceCents, r.VatIncluded ?? product.VatIncluded, r.InStock ?? product.InStock, r.ExpiryPolicy ?? product.ExpiryPolicy);
        var updated = product.Update(input, null);
        if (!updated.IsSuccess) return Result<SupplierProductResponse>.Fail(updated.Error!.Code, updated.Error.Message);

        await products.Save(ct);
        return Result<SupplierProductResponse>.Ok(ProductMapping.ToResponse(product));
    }
}
