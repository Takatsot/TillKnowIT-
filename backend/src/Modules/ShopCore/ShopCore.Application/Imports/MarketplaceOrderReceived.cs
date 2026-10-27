using Marketplace.Contracts;
using ShopCore.Domain;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Imports;

/// <summary>
/// M4-4: when a shop receives a marketplace order, its stock-in review is filled in for the owner: the products matched by barcode,
/// the units that arrived and what each cost. The owner checks it and confirms; nothing reaches stock until they do.
/// </summary>
internal sealed class MarketplaceOrderReceivedHandler(IImportStore store, TimeProvider clock) : IDomainEventHandler<OrderReceived>
{
    public async Task Handle(OrderReceived e, CancellationToken ct)
    {
        // The event is delivered at least once: one order gives one review.
        var url = $"marketplace://orders/{e.OrderId:N}";
        if (await store.ExistsByFile(e.ShopId, url, ct)) return;

        var raw = e.Lines.Select((l, i) => new RawImportLine(i + 1, l.Name, l.Barcode, l.Units, null, null, l.UnitCostCents)).ToList();
        var plan = ImportPlanner.Plan(raw, await store.Products(e.ShopId, ct));

        var import = new InvoiceImport(e.ShopId, ImportKind.Invoice, url, $"Order {e.Reference}", e.SupplierName, Guid.Empty, Guid.Empty, clock.GetUtcNow());
        import.Complete(PlanJson.Write(new StoredPlan(null, null, plan)));
        await store.Add(import, ct);
        await store.Save(ct);
    }
}
