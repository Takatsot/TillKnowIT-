using Catalogue.Contracts;
using Identity.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Sync;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Tests;

public class UploadBatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeStore : IUploadStore
    {
        public HashSet<Guid> Products { get; } = [];
        public Dictionary<Guid, Dictionary<Guid, int>> Sold { get; } = [];
        public HashSet<Guid> Sales { get; } = [];
        public HashSet<Guid> Movements { get; } = [];
        public HashSet<Guid> Cancellations { get; } = [];
        public HashSet<Guid> CancelledSales { get; } = [];
        public List<Sale> AddedSales { get; } = [];
        public List<StockMovement> AddedMovements { get; } = [];
        public List<SaleCancellation> AddedCancellations { get; } = [];
        public int Saves { get; private set; }

        public Task<HashSet<Guid>> ExistingSaleIds(IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult(ids.Where(Sales.Contains).ToHashSet());
        public Task<HashSet<Guid>> ExistingMovementIds(IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult(ids.Where(Movements.Contains).ToHashSet());
        public Task<HashSet<Guid>> ExistingCancellationIds(IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult(ids.Where(Cancellations.Contains).ToHashSet());
        public Task<HashSet<Guid>> CancelledSaleIds(IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult(ids.Where(CancelledSales.Contains).ToHashSet());
        public Task<HashSet<Guid>> ExistingProductIds(Guid shopId, IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult(ids.Where(Products.Contains).ToHashSet());
        public Task<Dictionary<Guid, Dictionary<Guid, int>>> SaleQuantities(Guid shopId, IEnumerable<Guid> ids, CancellationToken ct) =>
            Task.FromResult(ids.Where(Sold.ContainsKey).ToDictionary(i => i, i => new Dictionary<Guid, int>(Sold[i])));
        public Dictionary<Guid, Product> ProductEntities { get; } = [];
        public Dictionary<string, Guid> Barcodes { get; } = [];
        public List<Product> AddedProducts { get; } = [];

        public Task<Dictionary<Guid, Product>> GetProducts(IEnumerable<Guid> ids, CancellationToken ct) =>
            Task.FromResult(ids.Where(ProductEntities.ContainsKey).ToDictionary(i => i, i => ProductEntities[i]));
        public Task<Dictionary<string, Guid>> BarcodeOwners(Guid shopId, IEnumerable<string> barcodes, CancellationToken ct) =>
            Task.FromResult(barcodes.Where(Barcodes.ContainsKey).ToDictionary(b => b, b => Barcodes[b]));
        public void Add(Product product) => AddedProducts.Add(product);
        public void Add(Sale sale) => AddedSales.Add(sale);
        public void Add(StockMovement movement) => AddedMovements.Add(movement);
        public void Add(SaleCancellation cancellation) => AddedCancellations.Add(cancellation);
        public Task Save(CancellationToken ct) { Saves++; return Task.CompletedTask; }
    }

    private sealed class FakeAccess : ITillAccessChecker
    {
        public Dictionary<(Guid Staff, string Permission), Result> Denied { get; } = [];
        public int Calls { get; private set; }

        public Task<Result> Check(Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Denied.GetValueOrDefault((staffId, permission), Result.Ok()));
        }
    }

    private sealed class FakeLookups : ILookupChecker
    {
        public Task<bool> IsActive(string type, string code, CancellationToken ct) =>
            Task.FromResult(type == "categories" && code is "drinks" or "groceries");
    }

    private readonly FakeStore _store = new();
    private readonly FakeAccess _access = new();
    private readonly Guid _shop = Guid.NewGuid();
    private readonly Guid _device = Guid.NewGuid();
    private readonly Guid _cashier = Guid.NewGuid();
    private readonly Guid _manager = Guid.NewGuid();
    private readonly Guid _bread = Guid.NewGuid();

    public UploadBatchTests() => _store.Products.Add(_bread);

    private Task<Result<SyncUploadResponse>> Upload(List<SaleUpload>? sales = null, List<StockMovementUpload>? moves = null,
        List<SaleCancellationUpload>? cancels = null, List<ProductUpload>? products = null) =>
        new UploadBatchHandler(_store, _access, new FakeLookups(), new FakeTimeProvider(Now))
            .Handle(new UploadBatch(new SyncUploadRequest(_shop, _device, sales, moves, cancels, products)), default);

    private SaleUpload CashSale(Guid? id = null, Guid? product = null) => new(
        id ?? Guid.NewGuid(), _cashier, Now, "cash", 3600, 0, 5000, 1400, null,
        [new SaleLineUpload(product ?? _bread, "Bread", 2, 1800, 1400)]);

    private StockMovementUpload SaleMove(Guid saleId, int qty = -2) =>
        new(Guid.NewGuid(), _cashier, _bread, "sale", qty, Now, saleId, null, null, null, null);

    [Fact]
    public async Task Accepts_a_sale_with_its_stock_movement_and_saves_once()
    {
        var sale = CashSale();
        var result = await Upload([sale], [SaleMove(sale.Id)]);

        Assert.All(result.Value!.Results, r => Assert.Equal("accepted", r.Status));
        Assert.Single(_store.AddedSales);
        Assert.Single(_store.AddedMovements);
        Assert.Equal(1, _store.Saves);
        Assert.Equal(_device, _store.AddedSales[0].DeviceId);
    }

    [Fact]
    public async Task Sending_the_same_batch_twice_creates_no_duplicates()
    {
        var sale = CashSale();
        var move = SaleMove(sale.Id);
        await Upload([sale], [move]);
        _store.Sales.Add(sale.Id);
        _store.Movements.Add(move.Id);
        _store.Sold[sale.Id] = new() { [_bread] = 2 };

        var again = await Upload([sale], [move]);

        Assert.All(again.Value!.Results, r => Assert.Equal("duplicate", r.Status));
        Assert.Single(_store.AddedSales);
        Assert.Single(_store.AddedMovements);
    }

    [Fact]
    public async Task Same_record_twice_in_one_batch_is_stored_once()
    {
        var sale = CashSale();
        var result = await Upload([sale, sale]);

        Assert.Equal(["accepted", "duplicate"], result.Value!.Results.Select(r => r.Status));
        Assert.Single(_store.AddedSales);
    }

    [Fact]
    public async Task One_bad_record_does_not_block_the_good_ones()
    {
        var good = CashSale();
        var bad = CashSale() with { TotalCents = 1 };

        var result = await Upload([bad, good]);

        Assert.Equal("rejected", result.Value!.Results[0].Status);
        Assert.Equal("sale.total_mismatch", result.Value.Results[0].Code);
        Assert.Equal("accepted", result.Value.Results[1].Status);
        Assert.Single(_store.AddedSales);
    }

    [Fact]
    public async Task Rejects_when_the_till_or_staff_is_not_allowed()
    {
        _access.Denied[(_cashier, "sale.create")] = Result.Fail("permission.denied", "no");
        var result = await Upload([CashSale()]);

        Assert.Equal("permission.denied", result.Value!.Results.Single().Code);
        Assert.Empty(_store.AddedSales);
    }

    [Fact]
    public async Task Asks_identity_once_per_staff_and_permission_in_a_batch()
    {
        await Upload([CashSale(), CashSale(), CashSale()]);
        Assert.Equal(1, _access.Calls);
    }

    [Fact]
    public async Task Unknown_product_is_rejected_so_the_till_uploads_products_first()
    {
        var result = await Upload([CashSale(product: Guid.NewGuid())]);
        Assert.Equal("product.unknown", result.Value!.Results.Single().Code);
    }

    [Fact]
    public async Task Receiving_stock_needs_the_receive_permission()
    {
        _access.Denied[(_cashier, "stock.receive")] = Result.Fail("permission.denied", "no");
        var receive = new StockMovementUpload(Guid.NewGuid(), _cashier, _bread, "receive", 24, Now, null, 1400, null, "Makro", "INV-1");

        var result = await Upload(moves: [receive]);

        Assert.Equal("permission.denied", result.Value!.Results.Single().Code);
    }

    [Fact]
    public async Task Unknown_movement_type_and_wrong_sign_are_rejected()
    {
        var weird = new StockMovementUpload(Guid.NewGuid(), _cashier, _bread, "teleport", 1, Now, null, null, null, null, null);
        var wrongSign = new StockMovementUpload(Guid.NewGuid(), _cashier, _bread, "receive", -3, Now, null, null, null, null, null);

        var result = await Upload(moves: [weird, wrongSign]);

        Assert.Equal(["stock.type_invalid", "stock.quantity_sign"], result.Value!.Results.Select(r => r.Code));
    }

    [Fact]
    public async Task Sale_movement_must_match_a_known_sale_and_not_exceed_what_was_sold()
    {
        var sale = CashSale();
        var tooMany = SaleMove(sale.Id, qty: -9);
        var orphan = SaleMove(Guid.NewGuid());

        var result = await Upload([sale], [tooMany, orphan]);

        Assert.Equal("accepted", result.Value!.Results[0].Status);
        Assert.Equal("stock.exceeds_sale", result.Value.Results[1].Code);
        Assert.Equal("stock.sale_unknown", result.Value.Results[2].Code);
    }

    // ---- cancellation ----

    private SaleCancellationUpload Cancel(Guid saleId) => new(Guid.NewGuid(), _manager, saleId, "Customer changed mind", Now);

    [Fact]
    public async Task Manager_can_cancel_a_sale_and_restore_its_stock_in_the_same_batch()
    {
        var sale = CashSale();
        var restore = new StockMovementUpload(Guid.NewGuid(), _manager, _bread, "cancel", 2, Now, sale.Id, null, null, null, null);

        var result = await Upload([sale], [restore], [Cancel(sale.Id)]);

        Assert.All(result.Value!.Results, r => Assert.Equal("accepted", r.Status));
        Assert.Single(_store.AddedCancellations);
        Assert.Equal(2, _store.AddedMovements.Single().Quantity);
    }

    [Fact]
    public async Task Cancelling_needs_the_cancel_permission()
    {
        var sale = CashSale();
        _access.Denied[(_manager, "sale.cancel")] = Result.Fail("permission.denied", "no");

        var result = await Upload([sale], cancels: [Cancel(sale.Id)]);

        Assert.Equal("permission.denied", result.Value!.Results[1].Code);
        Assert.Empty(_store.AddedCancellations);
    }

    [Fact]
    public async Task A_sale_can_only_be_cancelled_once_even_from_two_tills()
    {
        var sale = CashSale();
        var first = Cancel(sale.Id);
        var second = Cancel(sale.Id);

        var result = await Upload([sale], cancels: [first, second]);

        Assert.Equal("accepted", result.Value!.Results[1].Status);
        Assert.Equal("cancellation.already_cancelled", result.Value.Results[2].Code);
    }

    [Fact]
    public async Task Cancelling_an_unknown_sale_is_rejected()
    {
        var result = await Upload(cancels: [Cancel(Guid.NewGuid())]);
        Assert.Equal("cancellation.sale_unknown", result.Value!.Results.Single().Code);
    }

    // ---- products ----

    private ProductUpload Prod(Guid? id = null, string? barcode = "6001234567890", DateTimeOffset? at = null, string category = "groceries",
        string name = "Bread", long price = 1800) =>
        new(id ?? Guid.NewGuid(), _manager, null, name, barcode, category, 1400, price, 5, null, true, at ?? Now);

    [Fact]
    public async Task Creates_a_product_and_a_sale_for_it_can_arrive_in_the_same_batch()
    {
        var fresh = Prod();
        var sale = CashSale(product: fresh.Id);

        var result = await Upload([sale], products: [fresh]);

        Assert.Equal(["accepted", "accepted"], result.Value!.Results.Select(r => r.Status));
        Assert.Equal("product", result.Value.Results[0].Kind);
        Assert.Single(_store.AddedProducts);
        Assert.Single(_store.AddedSales);
    }

    [Fact]
    public async Task Products_need_the_product_edit_permission()
    {
        _access.Denied[(_manager, "product.edit")] = Result.Fail("permission.denied", "no");
        var result = await Upload(products: [Prod()]);
        Assert.Equal("permission.denied", result.Value!.Results.Single().Code);
        Assert.Empty(_store.AddedProducts);
    }

    [Theory]
    [InlineData("", "product.name_invalid")]
    [InlineData("   ", "product.name_invalid")]
    public async Task Product_needs_a_name(string name, string code)
    {
        var result = await Upload(products: [Prod(name: name)]);
        Assert.Equal(code, result.Value!.Results.Single().Code);
    }

    [Fact]
    public async Task Bad_barcode_negative_price_and_unknown_category_are_rejected()
    {
        var result = await Upload(products: [
            Prod(barcode: "12ab"),
            Prod(barcode: null, price: -1),
            Prod(barcode: null, category: "weapons"),
        ]);

        Assert.Equal(["product.barcode_invalid", "product.price_invalid", "product.category_invalid"], result.Value!.Results.Select(r => r.Code));
    }

    [Fact]
    public async Task A_barcode_can_only_belong_to_one_product_in_a_shop()
    {
        _store.Barcodes["6001234567890"] = Guid.NewGuid();
        var taken = await Upload(products: [Prod()]);
        Assert.Equal("product.duplicate_barcode", taken.Value!.Results.Single().Code);

        // Two new products in one batch cannot share one either.
        _store.Barcodes.Clear();
        var both = await Upload(products: [Prod(barcode: "6009999999999"), Prod(barcode: "6009999999999")]);
        Assert.Equal(["accepted", "product.duplicate_barcode"], both.Value!.Results.Select(r => r.Code ?? r.Status));
    }

    [Fact]
    public async Task Products_without_a_barcode_are_fine_and_may_repeat()
    {
        var result = await Upload(products: [Prod(barcode: null), Prod(barcode: null)]);
        Assert.All(result.Value!.Results, r => Assert.Equal("accepted", r.Status));
    }

    [Fact]
    public async Task Editing_a_product_keeps_the_latest_change_and_drops_an_older_one()
    {
        var id = Guid.NewGuid();
        var existing = Product.Record(new ProductInput(id, _shop, _manager, _device, null, "Bread", "6001234567890", "groceries",
            1400, 1800, 5, null, true, Now.AddHours(-2)));
        _store.ProductEntities[id] = existing;

        var newer = await Upload(products: [Prod(id, at: Now.AddHours(-1), price: 1900)]);
        Assert.Equal("accepted", newer.Value!.Results.Single().Status);
        Assert.Equal(1900, existing.PriceCents);

        var older = await Upload(products: [Prod(id, at: Now.AddHours(-1.5), price: 1000)]);
        Assert.Equal("duplicate", older.Value!.Results.Single().Status);
        Assert.Equal("product.stale", older.Value.Results.Single().Code);
        Assert.Equal(1900, existing.PriceCents);
        Assert.Empty(_store.AddedProducts);
    }

    [Fact]
    public async Task Retrying_the_same_product_change_is_a_duplicate_not_an_error()
    {
        var id = Guid.NewGuid();
        var upload = Prod(id, at: Now.AddMinutes(-5));
        var first = await Upload(products: [upload]);
        _store.ProductEntities[id] = _store.AddedProducts.Single();

        var retry = await Upload(products: [upload]);

        Assert.Equal("accepted", first.Value!.Results.Single().Status);
        Assert.Equal("duplicate", retry.Value!.Results.Single().Status);
    }

    [Fact]
    public async Task A_product_id_from_another_shop_is_refused()
    {
        var id = Guid.NewGuid();
        _store.ProductEntities[id] = Product.Record(new ProductInput(id, Guid.NewGuid(), _manager, _device, null, "Other", null, "groceries",
            1, 2, 0, null, true, Now.AddDays(-1)));

        var result = await Upload(products: [Prod(id, at: Now)]);

        Assert.Equal("product.id_taken", result.Value!.Results.Single().Code);
    }

    [Fact]
    public async Task A_till_clock_far_in_the_future_cannot_win_every_later_clash()
    {
        var result = await Upload(products: [Prod(at: Now.AddDays(30))]);
        Assert.Equal("product.date_invalid", result.Value!.Results.Single().Code);
    }

    // ---- limits ----

    [Fact]
    public async Task More_than_500_records_is_refused()
    {
        var sales = Enumerable.Range(0, 501).Select(_ => CashSale()).ToList();
        var result = await Upload(sales);

        Assert.Equal("upload.too_large", result.Error!.Code);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task Empty_batch_is_fine()
    {
        var result = await Upload();
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Results);
    }
}
