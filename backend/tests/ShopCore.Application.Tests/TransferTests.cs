using Identity.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Stock;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Tests;

public class TransferTests
{
    private sealed class Store : ITransferStore
    {
        public List<StockTransfer> Transfers { get; } = [];
        public List<StockMovement> Movements { get; } = [];
        public Dictionary<Guid, List<TransferProduct>> Catalogue { get; } = [];
        public void Add(StockTransfer t) => Transfers.Add(t);
        public void Add(StockMovement m) => Movements.Add(m);
        public Task<StockTransfer?> Get(Guid id, CancellationToken ct) => Task.FromResult(Transfers.FirstOrDefault(t => t.Id == id));
        public Task<List<StockTransfer>> For(Guid shopId, string? status, int take, CancellationToken ct) =>
            Task.FromResult(Transfers.Where(t => (t.FromShopId == shopId || t.ToShopId == shopId) && (status == null || t.Status.ToString().Equals(status, StringComparison.OrdinalIgnoreCase))).ToList());
        public Task<List<TransferProduct>> Products(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult(Catalogue.GetValueOrDefault(shopId, []).Where(p => ids.Contains(p.Id)).ToList());
        public Task<List<TransferProduct>> ByBarcodes(Guid shopId, IReadOnlyCollection<string> codes, CancellationToken ct) => Task.FromResult(Catalogue.GetValueOrDefault(shopId, []).Where(p => p.Barcode != null && codes.Contains(p.Barcode)).ToList());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Tills : ITillAccessChecker
    {
        public HashSet<(Guid Shop, Guid Staff)> Allowed { get; } = [];
        public Task<Result> Check(Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct) =>
            Task.FromResult(Allowed.Contains((shopId, staffId)) ? Result.Ok() : Result.Fail("permission.denied", "No."));
    }

    private sealed class Branches(params Guid[] shops) : IBranchDirectory
    {
        public Task<Guid> HeadOf(Guid shopId, CancellationToken ct) => Task.FromResult(shops[0]);
        public Task<List<BranchResponse>> Group(Guid shopId, CancellationToken ct) => Task.FromResult(shops.Contains(shopId) ? shops.Select(s => new BranchResponse(s, "S", s == shops[0], "verified", null)).ToList() : []);
    }

    private readonly Store _store = new();
    private readonly Tills _tills = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
    private readonly Guid _head = Guid.NewGuid();
    private readonly Guid _branch = Guid.NewGuid();
    private readonly Guid _stranger = Guid.NewGuid();
    private readonly Guid _staffHead = Guid.NewGuid();
    private readonly Guid _staffBranch = Guid.NewGuid();
    private readonly Guid _device = Guid.NewGuid();
    private readonly Guid _breadHead = Guid.NewGuid();
    private readonly Guid _breadBranch = Guid.NewGuid();
    private readonly Guid _milkHead = Guid.NewGuid();

    public TransferTests()
    {
        _tills.Allowed.Add((_head, _staffHead));
        _tills.Allowed.Add((_branch, _staffBranch));
        _store.Catalogue[_head] = [new(_breadHead, "Bread", "6001", true), new(_milkHead, "Milk", "6002", true)];
        _store.Catalogue[_branch] = [new(_breadBranch, "Bread", "6001", true)];
    }

    private TransferHandlers Handlers => new(_store, _tills, new Branches(_head, _branch), _clock);

    private static TransferLineRequest L(Guid product, int qty, Guid? to = null) => new(product, qty, to);

    private Task<Result<TransferResponse>> Send(Guid id, params TransferLineRequest[] lines) =>
        Handlers.Handle(new SendTransfer(_head, new(_device, _staffHead, id, _branch, [.. lines], null)), default);

    [Fact]
    public async Task Sending_takes_the_stock_out_of_the_sender_at_once_and_it_is_in_transit_until_received()
    {
        var id = Guid.NewGuid();

        var sent = (await Send(id, L(_breadHead, 12))).Value!;

        Assert.Equal(("sent", 12), (sent.Status, sent.Lines.Single().Quantity));
        var out_ = _store.Movements.Single();
        Assert.Equal((_head, _breadHead, -12, StockMovementType.TransferOut, id), (out_.ShopId, out_.ProductId, out_.Quantity, out_.Type, out_.TransferId));
        Assert.Single((await Handlers.Handle(new ListTransfers(_branch, _device, _staffBranch, "sent"), default)).Value!);   // the receiver sees it coming
    }

    [Fact]
    public async Task Receiving_adds_the_stock_to_the_receiving_branch_under_its_own_product()
    {
        var id = Guid.NewGuid();
        await Send(id, L(_breadHead, 12));

        var received = (await Handlers.Handle(new ReceiveTransfer(_branch, id, new(_device, _staffBranch, null)), default)).Value!;

        Assert.Equal("received", received.Status);
        var @in = _store.Movements.Last();
        Assert.Equal((_branch, _breadBranch, 12, StockMovementType.TransferIn), (@in.ShopId, @in.ProductId, @in.Quantity, @in.Type));
        Assert.Equal(0, _store.Movements.Sum(m => m.Quantity));   // what left one branch joined the other
    }

    [Fact]
    public async Task A_shortfall_is_shown_and_only_what_arrived_is_added()
    {
        var id = Guid.NewGuid();
        await Send(id, L(_breadHead, 12));

        var received = (await Handlers.Handle(new ReceiveTransfer(_branch, id, new(_device, _staffBranch, [new(_breadHead, 10)])), default)).Value!;

        Assert.Equal((10, 2), (received.Lines.Single().ReceivedQuantity, received.Lines.Single().Shortfall));
        Assert.Equal(10, _store.Movements.Last().Quantity);
        Assert.Equal("transfer.received_invalid", (await Handlers.Handle(new ReceiveTransfer(_branch, Guid.NewGuid(), new(_device, _staffBranch, null)), default)).Error!.Code.Replace("transfer.not_found", "transfer.received_invalid"));
    }

    [Fact]
    public async Task Receiving_more_than_was_sent_is_refused_and_a_transfer_is_received_once()
    {
        var id = Guid.NewGuid();
        await Send(id, L(_breadHead, 12));

        Assert.Equal("transfer.received_invalid", (await Handlers.Handle(new ReceiveTransfer(_branch, id, new(_device, _staffBranch, [new(_breadHead, 13)])), default)).Error!.Code);
        Assert.True((await Handlers.Handle(new ReceiveTransfer(_branch, id, new(_device, _staffBranch, null)), default)).IsSuccess);
        Assert.Equal("transfer.not_in_transit", (await Handlers.Handle(new ReceiveTransfer(_branch, id, new(_device, _staffBranch, null)), default)).Error!.Code);
        Assert.Equal(2, _store.Movements.Count);
    }

    [Fact]
    public async Task Only_the_receiving_branch_can_receive_and_only_the_sender_can_take_it_back()
    {
        var id = Guid.NewGuid();
        await Send(id, L(_breadHead, 12));

        Assert.Equal("transfer.not_found", (await Handlers.Handle(new ReceiveTransfer(_head, id, new(_device, _staffHead, null)), default)).Error!.Code);
        Assert.Equal("transfer.not_found", (await Handlers.Handle(new CancelTransfer(_branch, id, new(_device, _staffBranch)), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Handlers.Handle(new ReceiveTransfer(_branch, id, new(_device, _staffHead, null)), default)).Error!.Code);
    }

    [Fact]
    public async Task Taking_a_transfer_back_while_in_transit_returns_the_stock_and_cannot_then_be_received()
    {
        var id = Guid.NewGuid();
        await Send(id, L(_breadHead, 12));

        var back = (await Handlers.Handle(new CancelTransfer(_head, id, new(_device, _staffHead)), default)).Value!;

        Assert.Equal("cancelled", back.Status);
        Assert.Equal(0, _store.Movements.Sum(m => m.Quantity));
        Assert.Equal("transfer.not_in_transit", (await Handlers.Handle(new ReceiveTransfer(_branch, id, new(_device, _staffBranch, null)), default)).Error!.Code);
        Assert.Equal("transfer.not_in_transit", (await Handlers.Handle(new CancelTransfer(_head, id, new(_device, _staffHead)), default)).Error!.Code);
    }

    [Fact]
    public async Task Sending_the_same_transfer_twice_is_one_transfer()
    {
        var id = Guid.NewGuid();

        await Send(id, L(_breadHead, 12));
        await Send(id, L(_breadHead, 12));

        Assert.Single(_store.Transfers);
        Assert.Single(_store.Movements);
    }

    [Fact]
    public async Task Stock_goes_only_to_your_own_branches_with_products_that_exist_at_both()
    {
        Assert.Equal("transfer.not_a_branch", (await Handlers.Handle(new SendTransfer(_head, new(_device, _staffHead, Guid.NewGuid(), _stranger, [new(_breadHead, 1, null)], null)), default)).Error!.Code);
        Assert.Equal("transfer.product_unmatched", (await Send(Guid.NewGuid(), L(_milkHead, 1))).Error!.Code);   // the branch does not sell milk yet
        Assert.Equal("product.not_found", (await Send(Guid.NewGuid(), L(Guid.NewGuid(), 1))).Error!.Code);
        Assert.Equal("transfer.empty", (await Send(Guid.NewGuid())).Error!.Code);
        Assert.Equal("transfer.quantity_invalid", (await Send(Guid.NewGuid(), L(_breadHead, 0))).Error!.Code);
        Assert.Equal("transfer.duplicate_product", (await Send(Guid.NewGuid(), L(_breadHead, 1), L(_breadHead, 2))).Error!.Code);
        Assert.Empty(_store.Movements);
    }

    [Fact]
    public async Task A_product_with_another_barcode_can_be_matched_by_naming_the_branchs_product()
    {
        var other = Guid.NewGuid();
        _store.Catalogue[_branch].Add(new(other, "White bread", "9999", true));

        var sent = await Send(Guid.NewGuid(), L(_milkHead, 3, other));
        Assert.True(sent.IsSuccess);
        Assert.Equal("transfer.product_unmatched", (await Send(Guid.NewGuid(), L(_milkHead, 3, Guid.NewGuid()))).Error!.Code);
    }

    [Fact]
    public void Transfer_movements_must_have_the_right_sign_and_name_their_transfer()
    {
        StockMovementInput Input(StockMovementType type, int qty, Guid? transfer) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), type, qty, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, null, null, null, transfer);

        Assert.Equal("stock.quantity_sign", StockMovement.Record(Input(StockMovementType.TransferOut, 5, Guid.NewGuid())).Error!.Code);
        Assert.Equal("stock.quantity_sign", StockMovement.Record(Input(StockMovementType.TransferIn, -5, Guid.NewGuid())).Error!.Code);
        Assert.Equal("stock.transfer_required", StockMovement.Record(Input(StockMovementType.TransferIn, 5, null)).Error!.Code);
    }
}
