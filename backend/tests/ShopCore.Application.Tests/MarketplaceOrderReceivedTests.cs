using Marketplace.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Imports;
using ShopCore.Domain;

namespace ShopCore.Application.Tests;

public class MarketplaceOrderReceivedTests
{
    private static readonly Guid Shop = Guid.NewGuid();
    private static readonly Guid Bread = Guid.NewGuid();

    private sealed class Store : IImportStore
    {
        public List<InvoiceImport> Imports { get; } = [];
        public Task Add(InvoiceImport i, CancellationToken ct) { Imports.Add(i); return Task.CompletedTask; }
        public Task<bool> ExistsByFile(Guid shopId, string fileUrl, CancellationToken ct) => Task.FromResult(Imports.Any(i => i.ShopId == shopId && i.FileUrl == fileUrl));
        public Task<InvoiceImport?> Get(Guid shopId, Guid id, CancellationToken ct) => Task.FromResult(Imports.FirstOrDefault(i => i.Id == id));
        public Task<List<ProductRef>> Products(Guid shopId, CancellationToken ct) => Task.FromResult(new List<ProductRef> { new(Bread, "White Bread", "6001111111111", 1400) });
        public Task<ImportMapping?> GetMapping(Guid shopId, string key, CancellationToken ct) => Task.FromResult<ImportMapping?>(null);
        public Task Add(ImportMapping m, CancellationToken ct) => Task.CompletedTask;
        public Task<List<InvoiceImport>> QueuedInvoices(int take, CancellationToken ct) => Task.FromResult(new List<InvoiceImport>());
        public Task Add(StockMovement m, CancellationToken ct) => Task.CompletedTask;
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private static OrderReceived Event(Guid? orderId = null) => new(orderId ?? Guid.NewGuid(), Shop, Guid.NewGuid(), "Fresh Foods", "AB12CD34",
        [new("Bread", "6001111111111", 48, 1000), new("Mystery item", "6007777777777", 12, 500)]);

    [Fact]
    public async Task A_received_order_becomes_a_ready_stock_in_review_matched_by_barcode()
    {
        var store = new Store();

        await new MarketplaceOrderReceivedHandler(store, new FakeTimeProvider()).Handle(Event(), default);

        var import = Assert.Single(store.Imports);
        Assert.Equal(("Fresh Foods", ImportStatus.Ready), (import.SupplierName, import.Status));
        var plan = PlanJson.Read(import.ResultJson)!.Lines;
        Assert.Equal(Bread, plan[0].ProductId);
        Assert.Equal((48, 1000L), (plan[0].Quantity, plan[0].UnitCostCents));
        Assert.Null(plan[1].ProductId);   // unknown barcode: the owner decides
    }

    [Fact]
    public async Task Delivering_the_same_event_twice_gives_one_review()
    {
        var store = new Store();
        var handler = new MarketplaceOrderReceivedHandler(store, new FakeTimeProvider());
        var e = Event();

        await handler.Handle(e, default);
        await handler.Handle(e, default);

        Assert.Single(store.Imports);
    }
}
