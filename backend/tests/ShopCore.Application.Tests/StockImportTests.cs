using System.Text;
using Identity.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Imports;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.Importing;
using TillKnowIT.SharedKernel;
using TillKnowIT.Storage;

namespace ShopCore.Application.Tests;

public class StockImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Bread = Guid.NewGuid();
    private static readonly Guid BrownBread = Guid.NewGuid();
    private static readonly Guid Coke = Guid.NewGuid();
    private static readonly Guid Milk1 = Guid.NewGuid();
    private static readonly Guid Milk2 = Guid.NewGuid();

    private static readonly List<ProductRef> Products =
    [
        new(Bread, "White Bread", "6001111111111", 1400),
        new(BrownBread, "Brown Bread", null, 1500),
        new(Coke, "Coke 330ml can", "6002222222222", 900),
        new(Milk1, "Fresh Milk 1l", null, 2100),
        new(Milk2, "fresh  milk 1L!", null, 2200),
    ];

    private static ImportLine One(RawImportLine raw) => ImportPlanner.Plan([raw], Products).Single();

    // ---- planning ----

    [Fact]
    public void A_barcode_matches_first_even_when_the_name_is_different()
    {
        var line = One(new RawImportLine(2, "Something else", "6001111111111", 12, null, null, 1400));
        Assert.Equal(Bread, line.ProductId);
        Assert.Equal("barcode", line.MatchedBy);
        Assert.Empty(line.Issues);
    }

    [Fact]
    public void Names_match_ignoring_case_spacing_and_punctuation_and_cases_become_units()
    {
        var line = One(new RawImportLine(2, "  coke 330ML  CAN ", null, 4, 24, 18000, null));

        Assert.Equal(Coke, line.ProductId);
        Assert.Equal("name", line.MatchedBy);
        Assert.Equal(96, line.Quantity);                     // 4 cases of 24
        Assert.Equal(750, line.UnitCostCents);               // R180.00 a case is R7.50 each
        Assert.Equal(24, line.UnitsPerCase);
    }

    [Theory]
    [InlineData(1000, 3, 333)]    // 333.33 rounds down
    [InlineData(1001, 3, 334)]    // 333.67 rounds up
    [InlineData(1001, 2, 501)]    // 500.5 rounds half up
    public void The_cost_per_unit_is_the_case_price_divided_by_the_case_size_to_the_nearest_cent(long caseCost, int perCase, long expected) =>
        Assert.Equal(expected, One(new RawImportLine(2, "Coke 330ml can", null, 1, perCase, caseCost, null)).UnitCostCents);

    [Fact]
    public void Without_a_case_size_the_quantity_is_units_and_a_case_price_is_taken_as_the_unit_price()
    {
        var line = One(new RawImportLine(2, "Coke 330ml can", null, 10, null, 900, null));
        Assert.Equal(10, line.Quantity);
        Assert.Equal(900, line.UnitCostCents);
    }

    [Fact]
    public void An_unknown_item_gets_suggestions_by_shared_words_not_a_guess()
    {
        var line = One(new RawImportLine(2, "Bread brown 700g", null, 6, null, null, 1500));

        Assert.Null(line.ProductId);
        Assert.Equal("none", line.MatchedBy);
        Assert.Contains(BrownBread, line.Candidates);
        Assert.Contains("no_match", line.Issues);
        Assert.True(ImportPlanner.NeedsAttention(line));
    }

    [Fact]
    public void Two_products_with_the_same_name_are_ambiguous_and_never_auto_matched()
    {
        var line = One(new RawImportLine(2, "Fresh Milk 1L", null, 6, null, null, 2100));

        Assert.Null(line.ProductId);
        Assert.Contains("ambiguous_name", line.Issues);
        Assert.Equal(2, line.Candidates.Count);
        Assert.Contains(Milk1, line.Candidates);
        Assert.Contains(Milk2, line.Candidates);
    }

    [Fact]
    public void Problems_are_flagged_for_the_owner_to_look_at()
    {
        var noQuantity = One(new RawImportLine(2, "Coke 330ml can", null, null, null, null, 900));
        var zeroQuantity = One(new RawImportLine(3, "Coke 330ml can", null, 0, null, null, 900));
        var noCost = One(new RawImportLine(4, "Coke 330ml can", null, 5, null, null, null));
        var nothing = One(new RawImportLine(5, "", null, 5, null, null, 100));

        Assert.Contains("quantity_invalid", noQuantity.Issues);
        Assert.Contains("quantity_invalid", zeroQuantity.Issues);
        Assert.Contains("cost_missing", noCost.Issues);
        Assert.Contains("name_or_barcode_missing", nothing.Issues);
        Assert.DoesNotContain("no_match", nothing.Issues);
        Assert.True(ImportPlanner.NeedsAttention(noQuantity));
        Assert.False(ImportPlanner.NeedsAttention(noCost));        // a missing cost can be left as it is
    }

    [Fact]
    public void Word_overlap_scores_the_share_of_words_in_common()
    {
        Assert.Equal(1.0, ImportPlanner.Overlap("Coke 330ml can", "can coke 330ml"));
        Assert.Equal(0.5, ImportPlanner.Overlap("bread", "brown bread"));
        Assert.Equal(0.0, ImportPlanner.Overlap("", "bread"));
        Assert.Equal(0.0, ImportPlanner.Overlap("milk", "bread"));
    }

    // ---- confirming ----

    private static ConfirmedLine Line(Guid product, int qty, long? cost = 1000) => new(product, qty, cost);

    [Fact]
    public void Confirming_makes_one_receive_movement_per_product_sharing_supplier_and_invoice()
    {
        var known = new HashSet<Guid> { Bread, Coke };
        var result = ImportConfirmation.ToMovements([Line(Bread, 12), Line(Coke, 24, 750), Line(Bread, 6, 1100)], known,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " Makro ", "INV-9", Now);

        var movements = result.Value!;
        Assert.Equal(2, movements.Count);
        var bread = movements.Single(m => m.ProductId == Bread);
        Assert.Equal(18, bread.Quantity);                    // two lines for one product are added together
        Assert.Equal(1100, bread.UnitCostCents);             // the later cost wins
        Assert.All(movements, m => { Assert.Equal(StockMovementType.Receive, m.Type); Assert.Equal("Makro", m.SupplierName); Assert.Equal("INV-9", m.InvoiceRef); });
    }

    [Fact]
    public void Confirming_refuses_unknown_products_bad_quantities_and_empty_lists()
    {
        var known = new HashSet<Guid> { Bread };
        string Code(params ConfirmedLine[] lines) => ImportConfirmation.ToMovements(lines, known, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, Now).Error!.Code;

        Assert.Equal("import.product_unknown", Code(Line(Coke, 1)));
        Assert.Equal("stock.quantity_sign", Code(Line(Bread, 0)));
        Assert.Equal("stock.quantity_sign", Code(Line(Bread, -4)));
        Assert.Equal("stock.cost_invalid", Code(Line(Bread, 1, -5)));
        Assert.Equal("import.no_lines", Code());
    }

    // ---- the flow ----

    private sealed class FakeStore : IImportStore
    {
        public List<InvoiceImport> Imports { get; } = [];
        public List<ImportMapping> Mappings { get; } = [];
        public List<StockMovement> Movements { get; } = [];
        public int Saves { get; private set; }
        public Task Add(InvoiceImport i, CancellationToken ct) { Imports.Add(i); return Task.CompletedTask; }
        public Task<InvoiceImport?> Get(Guid shopId, Guid id, CancellationToken ct) => Task.FromResult(Imports.FirstOrDefault(i => i.Id == id && i.ShopId == shopId));
        public Task<List<ProductRef>> Products(Guid shopId, CancellationToken ct) => Task.FromResult(StockImportTests.Products);
        public Task<ImportMapping?> GetMapping(Guid shopId, string key, CancellationToken ct) => Task.FromResult(Mappings.FirstOrDefault(m => m.ShopId == shopId && m.SupplierKey == key));
        public Task Add(ImportMapping m, CancellationToken ct) { Mappings.Add(m); return Task.CompletedTask; }
        public Task<List<InvoiceImport>> QueuedInvoices(int take, CancellationToken ct) => Task.FromResult(Imports.Where(i => i.Kind == ImportKind.Invoice && i.Status == ImportStatus.Queued).Take(take).ToList());
        public Task Add(StockMovement m, CancellationToken ct) { Movements.Add(m); return Task.CompletedTask; }
        public Task Save(CancellationToken ct) { Saves++; return Task.CompletedTask; }
    }

    private sealed class MemoryFiles : IFileStore
    {
        public Dictionary<string, StoredFile> Files { get; } = [];
        public Task<string> Save(string container, string name, string type, byte[] content, CancellationToken ct) { var u = $"tkfile://{container}/{Guid.NewGuid():N}"; Files[u] = new(content, type, name); return Task.FromResult(u); }
        public Task<StoredFile?> Open(string url, CancellationToken ct) => Task.FromResult(Files.GetValueOrDefault(url));
        public Task Delete(string url, CancellationToken ct) { Files.Remove(url); return Task.CompletedTask; }
    }

    private sealed class FakeAccess : ITillAccessChecker
    {
        public Result Answer { get; set; } = Result.Ok();
        public Task<Result> Check(Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct) => Task.FromResult(Answer);
    }

    private sealed class FakeReader(Func<InvoiceReading> reply) : IInvoiceReader
    {
        public Task<InvoiceReading> Read(StoredFile file, CancellationToken ct) => Task.FromResult(reply());
    }

    private readonly FakeStore _store = new();
    private readonly MemoryFiles _files = new();
    private readonly FakeAccess _access = new();
    private readonly Guid _shop = Guid.NewGuid();

    private StartStockImportHandler Start() => new(_store, _files, _access, new FakeTimeProvider(Now));
    private GetStockImportHandler Get() => new(_store);

    private StartStockImport Upload(string name, string csv, string? supplier = "Makro") =>
        new(_shop, Guid.NewGuid(), Guid.NewGuid(), name, "text/csv", Encoding.UTF8.GetBytes(csv), supplier);

    private const string SupplierCsv = "EAN;Description;Cases;Case Size;Case Price\n6002222222222;Coke 330ml can;4;24;R180,00\n;Mystery sweets;2;12;R60,00\n";

    [Fact]
    public async Task A_supplier_csv_is_read_at_once_matched_and_waiting_for_review_with_nothing_saved_to_stock()
    {
        var started = await Start().Handle(Upload("makro.csv", SupplierCsv), default);

        Assert.Equal("ready", started.Value!.Status);
        var review = (await Get().Handle(new GetStockImport(_shop, started.Value.ImportId), default)).Value!;
        Assert.Equal("sheet", review.Kind);
        Assert.Equal("EAN", review.Mapping!["barcode"]);
        Assert.Equal("Case Price", review.Mapping["case_cost"]);

        var coke = review.Lines[0];
        Assert.Equal(Coke, coke.ProductId);
        Assert.Equal(96, coke.Quantity);
        Assert.Equal(750, coke.UnitCostCents);
        Assert.Equal(2, coke.Row);
        Assert.Null(review.Lines[1].ProductId);
        Assert.Contains("no_match", review.Lines[1].Issues);

        Assert.Empty(_store.Movements);                               // nothing is saved until the owner confirms
        Assert.Single(_files.Files);
    }

    [Fact]
    public async Task Only_someone_who_may_receive_stock_can_upload_and_bad_files_are_refused_before_anything_is_kept()
    {
        _access.Answer = Result.Fail("permission.denied", "no");
        Assert.Equal("permission.denied", (await Start().Handle(Upload("a.csv", SupplierCsv), default)).Error!.Code);
        _access.Answer = Result.Ok();

        Assert.Equal("import.empty", (await Start().Handle(Upload("a.csv", ""), default)).Error!.Code);
        Assert.Equal("file.content_mismatch", (await Start().Handle(new StartStockImport(_shop, Guid.NewGuid(), Guid.NewGuid(), "a.pdf", "application/pdf", "MZ-malware"u8.ToArray(), null), default)).Error!.Code);
        Assert.Equal("file.type_invalid", (await Start().Handle(new StartStockImport(_shop, Guid.NewGuid(), Guid.NewGuid(), "a.exe", "application/octet-stream", [1, 2, 3], null), default)).Error!.Code);
        Assert.Empty(_store.Imports);
        Assert.Empty(_files.Files);
    }

    [Fact]
    public async Task Fixing_the_columns_re_works_the_review_and_the_choice_is_remembered_for_that_supplier()
    {
        var csv = "Code;Stuff;Packs;Per Pack;Cost\n6002222222222;Coke 330ml can;2;12;R90,00\n";
        var id = (await Start().Handle(Upload("makro.csv", csv), default)).Value!.ImportId;

        var fixedMapping = new Dictionary<string, string> { ["barcode"] = "Code", ["name"] = "Stuff", ["quantity"] = "Packs", ["units_per_case"] = "Per Pack", ["case_cost"] = "Cost" };
        var remap = await new RemapStockImportHandler(_store, _files, _access, new FakeTimeProvider(Now))
            .Handle(new RemapStockImport(_shop, id, new RemapStockImportRequest(_shop, Guid.NewGuid(), Guid.NewGuid(), fixedMapping)), default);
        Assert.True(remap.IsSuccess);

        var line = (await Get().Handle(new GetStockImport(_shop, id), default)).Value!.Lines.Single();
        Assert.Equal((24, 750L), (line.Quantity, line.UnitCostCents));

        // Next time Makro sends the same layout, the columns are already chosen.
        var next = (await Start().Handle(Upload("makro-nov.csv", csv, supplier: " MAKRO "), default)).Value!.ImportId;
        var review = (await Get().Handle(new GetStockImport(_shop, next), default)).Value!;
        Assert.Equal("Packs", review.Mapping!["quantity"]);
        Assert.Single(_store.Mappings);
    }

    [Fact]
    public async Task A_mapping_that_names_a_missing_column_or_an_unknown_field_is_refused()
    {
        var id = (await Start().Handle(Upload("makro.csv", SupplierCsv), default)).Value!.ImportId;
        var remap = new RemapStockImportHandler(_store, _files, _access, new FakeTimeProvider(Now));
        RemapStockImport Ask(Dictionary<string, string> m) => new(_shop, id, new(_shop, Guid.NewGuid(), Guid.NewGuid(), m));

        Assert.Equal("import.mapping_invalid", (await remap.Handle(Ask(new() { ["name"] = "No such column" }), default)).Error!.Code);
        Assert.Equal("import.mapping_invalid", (await remap.Handle(Ask(new() { ["colour"] = "Description" }), default)).Error!.Code);
    }

    // ---- confirm ----

    private ConfirmStockImportHandler Confirm() => new(_store, _access, new FakeTimeProvider(Now));

    private ConfirmStockImport Confirmation(Guid importId, params ConfirmedLineRequest[] lines) =>
        new(_shop, importId, new ConfirmStockImportRequest(_shop, Guid.NewGuid(), Guid.NewGuid(), null, "INV-7", [.. lines]));

    [Fact]
    public async Task Confirming_receives_the_reviewed_lines_once_and_only_once()
    {
        var id = (await Start().Handle(Upload("makro.csv", SupplierCsv), default)).Value!.ImportId;
        var request = Confirmation(id, new ConfirmedLineRequest(Coke, 96, 750), new ConfirmedLineRequest(Bread, 12, 1400));

        var first = await Confirm().Handle(request, default);
        var second = await Confirm().Handle(request, default);

        Assert.Equal(2, first.Value);
        Assert.Equal(2, _store.Movements.Count);
        Assert.All(_store.Movements, m => { Assert.Equal(StockMovementType.Receive, m.Type); Assert.Equal("Makro", m.SupplierName); Assert.Equal("INV-7", m.InvoiceRef); });
        Assert.Equal("import.already_confirmed", second.Error!.Code);
        Assert.Equal(2, _store.Movements.Count);
    }

    [Fact]
    public async Task A_bad_line_confirms_nothing_and_leaves_the_import_ready_to_try_again()
    {
        var id = (await Start().Handle(Upload("makro.csv", SupplierCsv), default)).Value!.ImportId;

        var bad = await Confirm().Handle(Confirmation(id, new ConfirmedLineRequest(Guid.NewGuid(), 5, 100)), default);

        Assert.Equal("import.product_unknown", bad.Error!.Code);
        Assert.Empty(_store.Movements);
        Assert.Equal(ImportStatus.Ready, _store.Imports.Single().Status);
    }

    [Fact]
    public async Task A_photo_that_has_not_been_read_yet_cannot_be_confirmed()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 };
        var started = await Start().Handle(new StartStockImport(_shop, Guid.NewGuid(), Guid.NewGuid(), "invoice.jpg", "image/jpeg", jpeg, "Makro"), default);
        Assert.Equal("queued", started.Value!.Status);

        var result = await Confirm().Handle(Confirmation(started.Value.ImportId, new ConfirmedLineRequest(Bread, 1, 1)), default);
        Assert.Equal("import.not_ready", result.Error!.Code);
    }

    // ---- reading invoices (worker) ----

    private async Task<Guid> QueuePhoto()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 };
        return (await Start().Handle(new StartStockImport(_shop, Guid.NewGuid(), Guid.NewGuid(), "invoice.jpg", "image/jpeg", jpeg, null), default)).Value!.ImportId;
    }

    [Fact]
    public async Task A_read_invoice_becomes_a_review_table_like_any_other()
    {
        var id = await QueuePhoto();
        var reader = new FakeReader(() => new InvoiceReading(true, [new RawImportLine(1, "Coke 330ml can", null, 2, 24, 18000, null)]));

        var done = await new ReadQueuedInvoicesHandler(_store, _files, [reader]).Handle(new ReadQueuedInvoices(), default);

        Assert.Equal(1, done.Value);
        var review = (await Get().Handle(new GetStockImport(_shop, id), default)).Value!;
        Assert.Equal("ready", review.Status);
        Assert.Equal((Coke, 48, 750L), (review.Lines[0].ProductId!.Value, review.Lines[0].Quantity, review.Lines[0].UnitCostCents!.Value));
        Assert.Empty(_store.Movements);
    }

    [Fact]
    public async Task Without_a_reader_or_when_it_fails_the_import_says_so_instead_of_staying_queued_for_ever()
    {
        var noReader = await QueuePhoto();
        await new ReadQueuedInvoicesHandler(_store, _files, []).Handle(new ReadQueuedInvoices(), default);
        var review = (await Get().Handle(new GetStockImport(_shop, noReader), default)).Value!;
        Assert.Equal("failed", review.Status);
        Assert.Contains("No invoice reader", review.Error);

        var failing = await QueuePhoto();
        await new ReadQueuedInvoicesHandler(_store, _files, [new FakeReader(() => new InvoiceReading(false, [], Error: "blurry"))]).Handle(new ReadQueuedInvoices(), default);
        Assert.Equal("blurry", (await Get().Handle(new GetStockImport(_shop, failing), default)).Value!.Error);
    }

    [Fact]
    public async Task A_reader_that_crashes_fails_that_import_without_stopping_the_worker()
    {
        var id = await QueuePhoto();
        var crashing = new FakeReader(() => throw new InvalidOperationException("service down"));

        var result = await new ReadQueuedInvoicesHandler(_store, _files, [crashing]).Handle(new ReadQueuedInvoices(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("failed", (await Get().Handle(new GetStockImport(_shop, id), default)).Value!.Status);
    }
}
