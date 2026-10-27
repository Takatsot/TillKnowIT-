using Identity.Contracts;
using Marketplace.Application.Offers;
using Marketplace.Application.Orders;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using Messaging.Contracts;
using Microsoft.Extensions.Time.Testing;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Tests;

public class OrderFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);   // Monday

    private sealed class FakeSuppliers : ISupplierStore, ISupplierProductStore
    {
        public Dictionary<Guid, Supplier> Suppliers { get; } = [];
        public List<SupplierProduct> Products { get; } = [];
        public Task Add(Supplier s, CancellationToken ct) { Suppliers[s.Id] = s; return Task.CompletedTask; }
        public Task<Supplier?> Get(Guid id, CancellationToken ct) => Task.FromResult(Suppliers.GetValueOrDefault(id));
        public Task<List<Supplier>> Pending(CancellationToken ct) => Task.FromResult(new List<Supplier>());
        public Task<List<SupplierArea>> Areas(Guid id, CancellationToken ct) => Task.FromResult(new List<SupplierArea>());
        public Task ReplaceAreas(Guid id, List<SupplierArea> a, CancellationToken ct) => Task.CompletedTask;
        public Task Save(CancellationToken ct) => Task.CompletedTask;
        public Task<Dictionary<string, SupplierProduct>> ByBarcode(Guid id, CancellationToken ct) => Task.FromResult(new Dictionary<string, SupplierProduct>());
        Task<SupplierProduct?> ISupplierProductStore.Get(Guid id, CancellationToken ct) => Task.FromResult(Products.FirstOrDefault(p => p.Id == id));
        public Task<List<SupplierProduct>> List(Guid id, CancellationToken ct) => Task.FromResult(Products.Where(p => p.SupplierId == id).ToList());
        public Task Add(SupplierProduct p, CancellationToken ct) { Products.Add(p); return Task.CompletedTask; }
    }

    private sealed class FakeOrders : IOrderStore
    {
        public List<PurchaseOrder> Orders { get; } = [];
        public List<LastPrice> Prices { get; } = [];
        public Task Add(PurchaseOrder o, CancellationToken ct) { Orders.Add(o); return Task.CompletedTask; }
        public Task<PurchaseOrder?> Get(Guid id, CancellationToken ct) => Task.FromResult(Orders.FirstOrDefault(o => o.Id == id));
        public Task<List<PurchaseOrder>> ForShop(Guid shopId, CancellationToken ct) => Task.FromResult(Orders.Where(o => o.ShopId == shopId).ToList());
        public Task<List<PurchaseOrder>> ForSupplier(Guid id, OrderStatus? s, CancellationToken ct) => Task.FromResult(Orders.Where(o => o.SupplierId == id && (s == null || o.Status == s)).ToList());
        public Task<LastPrice?> GetLastPrice(Guid shop, Guid supplier, string barcode, CancellationToken ct) => Task.FromResult(Prices.FirstOrDefault(p => p.ShopId == shop && p.SupplierId == supplier && p.Barcode == barcode));
        public Task Add(LastPrice p, CancellationToken ct) { Prices.Add(p); return Task.CompletedTask; }
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeShopMarketplace : IShopMarketplaceStore
    {
        public List<ShopSupplier> Usual { get; } = [];
        public Task<SharingSetting?> GetSharing(Guid s, CancellationToken ct) => Task.FromResult<SharingSetting?>(null);
        public Task Add(SharingSetting s, CancellationToken ct) => Task.CompletedTask;
        public Task<ShopSupplier?> GetShopSupplier(Guid shop, Guid supplier, CancellationToken ct) => Task.FromResult(Usual.FirstOrDefault(u => u.ShopId == shop && u.SupplierId == supplier));
        public Task Add(ShopSupplier s, CancellationToken ct) { Usual.Add(s); return Task.CompletedTask; }
        public Task<MarketplaceSetting?> GetSetting(string key, CancellationToken ct) => Task.FromResult<MarketplaceSetting?>(null);
        public Task Add(MarketplaceSetting s, CancellationToken ct) => Task.CompletedTask;
        public Task<Dictionary<string, string>> Settings(CancellationToken ct) => Task.FromResult(new Dictionary<string, string>());
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeAccess : IOrganisationAccess
    {
        public HashSet<(Guid, Guid, string)> Granted { get; } = [];
        public Task<bool> Has(Guid p, Guid o, string perm, CancellationToken ct) => Task.FromResult(Granted.Contains((p, o, perm)));
    }

    private sealed class FakeShops : IShopDirectory
    {
        public Task<ShopInfo?> Get(Guid shopId, CancellationToken ct) => Task.FromResult<ShopInfo?>(new ShopInfo(shopId, "Mama's Spaza", "verified", null, "0821110000", null, "zu", "jhb"));
        public Task<List<Guid>> VerifiedShopIds(CancellationToken ct) => Task.FromResult(new List<Guid>());
    }

    private sealed class FakeQueue : IMessageQueue
    {
        public List<QueueMessageRequest> Sent { get; } = [];
        public bool Fail { get; set; }
        public Task<Result<QueueMessageResult>> Queue(QueueMessageRequest r, CancellationToken ct)
        {
            if (Fail) return Task.FromResult(Result<QueueMessageResult>.Fail("destination.phone_invalid", "bad number"));
            Sent.Add(r);
            return Task.FromResult(Result<QueueMessageResult>.Ok(new QueueMessageResult(Guid.NewGuid(), false)));
        }
    }

    private readonly FakeSuppliers _suppliers = new();
    private readonly FakeOrders _orders = new();
    private readonly FakeShopMarketplace _shopMarketplace = new();
    private readonly FakeAccess _access = new();
    private readonly FakeQueue _queue = new();
    private readonly Guid _shop = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _supplierPerson = Guid.NewGuid();
    private readonly Guid _supplierOrg = Guid.NewGuid();
    private readonly Supplier _supplier;
    private readonly SupplierProduct _coke;
    private readonly SupplierProduct _fanta;

    public OrderFlowTests()
    {
        _supplier = Supplier.Apply(new SupplierApplication(_supplierPerson, _supplierOrg, "Fresh Foods", null, "Sipho", "0821234567", "0825550000", "orders@fresh.co.za", 1), Now).Value!;
        _supplier.Verify(Guid.NewGuid(), SupplierVerification.Items, Now);
        _supplier.SetTerms(50_000, 5_000, PaymentTerms.Both);
        _suppliers.Suppliers[_supplier.Id] = _supplier;
        _coke = SupplierProduct.Create(_supplier.Id, null, new SupplierProductInput("6001111111111", "Coke 330ml", 24, 24000, true, true, null), Now).Value!;
        _fanta = SupplierProduct.Create(_supplier.Id, null, new SupplierProductInput("6002222222222", "Fanta 330ml", 24, 22000, false, true, null), Now).Value!;
        _suppliers.Products.AddRange([_coke, _fanta]);
        _access.Granted.Add((_owner, _shop, "stock.receive"));
        _access.Granted.Add((_supplierPerson, _supplierOrg, "supplier.orders"));
    }

    private CreateOrderHandlers Create() => new(_suppliers, _suppliers, _orders, _access, new FakeTimeProvider(Now));
    private OrderNotifier Notifier() => new(new FakeShops(), _queue);
    private SendOrderHandlers Send() => new(_suppliers, _orders, _access, new FakeShops(), Notifier(), new FakeTimeProvider(Now));
    private RespondToOrderHandler Respond() => new(_suppliers, _orders, _access, Notifier(), new FakeTimeProvider(Now));
    private DeliveryHandlers Delivery() => new(_suppliers, _orders, _shopMarketplace, _access, new FakeTimeProvider(Now));
    private OrderQueries Queries() => new(_suppliers, _orders, _access);

    private CreateOrderRequest Request(Guid? supplier = null, string payment = "on_delivery", params OrderLineRequest[] lines) =>
        new(_owner, supplier ?? _supplier.Id, payment, lines.Length > 0 ? [.. lines] : [new(_coke.Id, null, null, 4, null, null)]);

    private async Task<OrderResponse> Drafted(params OrderLineRequest[] lines) => (await Create().Handle(new CreateOrder(_shop, Request(lines: lines)), default)).Value!;

    private async Task<OrderResponse> SentToApp(params OrderLineRequest[] lines)
    {
        var draft = await Drafted(lines);
        return (await Send().Handle(new SendOrder(draft.Id, new(_owner, "app")), default)).Value!;
    }

    private static List<LineDecisionRequest> ConfirmAll(OrderResponse o) => o.Lines.Select(l => new LineDecisionRequest(l.Id, "confirm", null, null)).ToList();

    // ---- create ----

    [Fact]
    public async Task An_order_copies_each_products_name_price_case_size_and_VAT_and_the_suppliers_terms()
    {
        var order = await Drafted(new OrderLineRequest(_coke.Id, null, null, 4, null, null), new OrderLineRequest(_fanta.Id, null, null, 2, null, null));

        Assert.Equal(("draft", "owing", "on_delivery", "Fresh Foods"), (order.Status, order.PaymentStatus, order.PaymentMethod, order.SupplierName));
        Assert.Equal(["Coke 330ml", "Fanta 330ml"], order.Lines.Select(l => l.Name));
        Assert.Equal(96000 + 44000, order.SubtotalCents);
        Assert.Equal(6600, order.VatCents);                       // Fanta's price leaves VAT out
        Assert.Equal(96000 + 44000 + 6600 + 5000, order.TotalCents);
        Assert.Equal(50_000, order.MinOrderCents);
        Assert.False(order.BelowMinimum);
        Assert.Equal(8, order.Reference.Length);
    }

    [Fact]
    public async Task Only_someone_at_the_shop_can_order_and_only_from_a_trading_supplier_with_its_own_products_that_are_in_stock()
    {
        Assert.Equal("permission.denied", (await Create().Handle(new CreateOrder(_shop, Request() with { CallerPersonId = Guid.NewGuid() }), default)).Error!.Code);
        Assert.Equal("supplier.not_found", (await Create().Handle(new CreateOrder(_shop, Request(Guid.NewGuid())), default)).Error!.Code);
        Assert.Equal("order.product_unknown", (await Create().Handle(new CreateOrder(_shop, Request(lines: new OrderLineRequest(Guid.NewGuid(), null, null, 1, null, null))), default)).Error!.Code);
        Assert.Equal("order.product_unknown", (await Create().Handle(new CreateOrder(_shop, Request(lines: new OrderLineRequest(null, "Typed in", null, 1, null, null))), default)).Error!.Code);

        _coke.Update(new SupplierProductInput("6001111111111", "Coke 330ml", 24, 24000, true, false, null), null);
        Assert.Equal("order.out_of_stock", (await Create().Handle(new CreateOrder(_shop, Request()), default)).Error!.Code);

        _supplier.Suspend();
        Assert.Equal("supplier.not_found", (await Create().Handle(new CreateOrder(_shop, Request(lines: new OrderLineRequest(_fanta.Id, null, null, 1, null, null))), default)).Error!.Code);
        Assert.Empty(_orders.Orders);
    }

    [Fact]
    public async Task The_supplier_terms_decide_which_payment_ways_the_shop_may_choose()
    {
        _supplier.SetTerms(0, 0, PaymentTerms.OnDelivery);
        Assert.Equal("order.payment_not_offered", (await Create().Handle(new CreateOrder(_shop, Request(payment: "in_app")), default)).Error!.Code);
        Assert.True((await Create().Handle(new CreateOrder(_shop, Request(payment: "on_delivery")), default)).IsSuccess);

        _supplier.SetTerms(0, 0, PaymentTerms.InApp);
        Assert.Equal("order.payment_not_offered", (await Create().Handle(new CreateOrder(_shop, Request(payment: "on_delivery")), default)).Error!.Code);
        Assert.True((await Create().Handle(new CreateOrder(_shop, Request(payment: "in_app")), default)).IsSuccess);

        Assert.Equal("order.payment_invalid", (await Create().Handle(new CreateOrder(_shop, Request(payment: "barter")), default)).Error!.Code);
    }

    [Fact]
    public async Task A_supplier_the_shop_added_itself_takes_typed_in_lines_and_is_paid_on_delivery_only()
    {
        var mine = Supplier.Unlisted(_shop, "Cash and carry", "0825551111", "0825551111", null, Now).Value!;
        _suppliers.Suppliers[mine.Id] = mine;

        var order = await Create().Handle(new CreateOrder(_shop, new(_owner, mine.Id, "on_delivery", [new(null, "Sugar 2kg", "6003333333333", 5, 12, 9000)])), default);

        Assert.Equal(("Sugar 2kg", 12, 9000L), (order.Value!.Lines[0].Name, order.Value.Lines[0].UnitsPerCase, order.Value.Lines[0].CasePriceCents));
        Assert.Equal("order.payment_not_offered", (await Create().Handle(new CreateOrder(_shop, new(_owner, mine.Id, "in_app", [new(null, "Sugar", null, 1, null, null)])), default)).Error!.Code);
        Assert.Equal("order.line_name", (await Create().Handle(new CreateOrder(_shop, new(_owner, mine.Id, "on_delivery", [new(null, " ", null, 1, null, null)])), default)).Error!.Code);

        var someoneElses = Supplier.Unlisted(Guid.NewGuid(), "Not mine", "0825552222", null, null, Now).Value!;
        _suppliers.Suppliers[someoneElses.Id] = someoneElses;
        Assert.Equal("supplier.not_found", (await Create().Handle(new CreateOrder(_shop, new(_owner, someoneElses.Id, "on_delivery", [new(null, "Sugar", null, 1, null, null)])), default)).Error!.Code);
    }

    [Fact]
    public async Task Building_from_the_order_list_makes_one_draft_per_supplier_and_warns_about_the_minimum()
    {
        var other = Supplier.Apply(new SupplierApplication(Guid.NewGuid(), Guid.NewGuid(), "Cheap Wholesale", null, "Joe", "0825558888", null, null, 1), Now).Value!;
        other.Verify(Guid.NewGuid(), SupplierVerification.Items, Now);
        other.SetTerms(100_000, 0, PaymentTerms.Both);
        _suppliers.Suppliers[other.Id] = other;
        var rice = SupplierProduct.Create(other.Id, null, new SupplierProductInput("6009999999999", "Rice 2kg", 6, 12000, true, true, null), Now).Value!;
        _suppliers.Products.Add(rice);

        var built = (await Create().Handle(new BuildOrders(_shop, new(_owner, "on_delivery", [new(_supplier.Id, _coke.Id, 2), new(_supplier.Id, _fanta.Id, 1), new(other.Id, rice.Id, 1)])), default)).Value!;

        Assert.Equal(2, built.Count);
        Assert.Equal(2, built.Single(b => b.Order.SupplierName == "Fresh Foods").Order.Lines.Count);
        var small = built.Single(b => b.Order.SupplierName == "Cheap Wholesale");
        Assert.Contains("wants at least R1000.00", small.Warning);
        Assert.Null(built.Single(b => b.Order.SupplierName == "Fresh Foods").Warning is { } w && w.Length == 0 ? "" : null);
        Assert.All(built, b => Assert.Equal("draft", b.Order.Status));
        Assert.Equal(2, _orders.Orders.Count);
    }

    [Fact]
    public async Task Building_needs_something_to_order()
    {
        Assert.Equal("order.no_lines", (await Create().Handle(new BuildOrders(_shop, new(_owner, "on_delivery", [])), default)).Error!.Code);
    }

    // ---- send ----

    [Fact]
    public async Task Sending_in_the_app_puts_it_in_the_suppliers_inbox_without_a_message()
    {
        var order = await SentToApp();

        Assert.Equal(("sent", "app"), (order.Status, order.Channel));
        Assert.Empty(_queue.Sent);
        var inbox = (await Queries().Handle(new ListSupplierOrders(_supplier.Id, _supplierPerson, null), default)).Value!;
        Assert.Equal([order.Id], inbox.Select(o => o.Id));
    }

    [Fact]
    public async Task Sending_by_whatsapp_or_email_messages_the_supplier_with_the_lines()
    {
        var draft = await Drafted(new OrderLineRequest(_coke.Id, null, null, 4, null, null));

        var sent = await Send().Handle(new SendOrder(draft.Id, new(_owner, "WhatsApp")), default);

        Assert.Equal("sent", sent.Value!.Status);
        var message = Assert.Single(_queue.Sent);
        Assert.Equal(("order.new", "whatsapp", "0825550000"), (message.TemplateCode, message.Channel, message.Destination));
        Assert.Equal("Mama's Spaza", message.Values["shop"]);
        Assert.Contains("4 x Coke 330ml (24 per case) @ R240.00 a case", message.Values["lines"]);
        Assert.Equal("payable on delivery", message.Values["payment"]);
        Assert.Equal($"order:{draft.Id:N}:new", message.DedupeKey);
    }

    [Fact]
    public async Task An_order_that_cannot_be_delivered_to_the_supplier_does_not_look_sent()
    {
        var draft = await Drafted();
        _queue.Fail = true;

        var result = await Send().Handle(new SendOrder(draft.Id, new(_owner, "email")), default);

        Assert.Equal("destination.phone_invalid", result.Error!.Code);
        Assert.Empty(_queue.Sent);   // nothing is saved on failure, so the stored order stays a draft
    }

    [Fact]
    public async Task The_app_channel_needs_a_supplier_on_the_app_and_the_other_channels_need_that_contact_on_file()
    {
        var offApp = Supplier.Unlisted(_shop, "Cash and carry", "0825551111", null, null, Now).Value!;
        _suppliers.Suppliers[offApp.Id] = offApp;
        var draft = (await Create().Handle(new CreateOrder(_shop, new(_owner, offApp.Id, "on_delivery", [new(null, "Sugar", null, 2, null, 6000)])), default)).Value!;

        Assert.Equal("order.supplier_not_on_app", (await Send().Handle(new SendOrder(draft.Id, new(_owner, "app")), default)).Error!.Code);
        Assert.Equal("order.supplier_unreachable", (await Send().Handle(new SendOrder(draft.Id, new(_owner, "whatsapp")), default)).Error!.Code);
        Assert.Equal("order.supplier_unreachable", (await Send().Handle(new SendOrder(draft.Id, new(_owner, "email")), default)).Error!.Code);
        Assert.Equal("order.channel_invalid", (await Send().Handle(new SendOrder(draft.Id, new(_owner, "pigeon")), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Send().Handle(new SendOrder(draft.Id, new(Guid.NewGuid(), "app")), default)).Error!.Code);
    }

    [Fact]
    public async Task An_order_is_sent_once_and_a_shop_can_cancel_until_the_supplier_confirms()
    {
        var order = await SentToApp();
        Assert.Equal("order.already_sent", (await Send().Handle(new SendOrder(order.Id, new(_owner, "app")), default)).Error!.Code);

        Assert.True((await Send().Handle(new CancelOrder(order.Id, _owner), default)).IsSuccess);
        Assert.Equal("order.cannot_cancel", (await Send().Handle(new CancelOrder(order.Id, _owner), default)).Error!.Code.Replace("order.cannot_cancel", "order.cannot_cancel"));
    }

    // ---- reply ----

    [Fact]
    public async Task The_supplier_confirms_with_a_date_and_the_shop_is_told_what_changed()
    {
        var order = await SentToApp(new OrderLineRequest(_coke.Id, null, null, 4, null, null), new OrderLineRequest(_fanta.Id, null, null, 2, null, null));
        var decisions = new List<LineDecisionRequest> { new(order.Lines[0].Id, "change", 3, 24500), new(order.Lines[1].Id, "decline", null, null) };

        var reply = await Respond().Handle(new RespondToOrder(order.Id, new(_supplierPerson, decisions, new DateOnly(2026, 10, 28))), default);

        Assert.Equal(("confirmed", new DateOnly(2026, 10, 28), true), (reply.Value!.Status, reply.Value.DeliveryDate, reply.Value.HasChanges));
        var message = Assert.Single(_queue.Sent);
        Assert.Equal(("order.update", "whatsapp", "zu"), (message.TemplateCode, message.Channel, message.Language));
        Assert.Contains("Confirmed for delivery on 2026-10-28", message.Values["summary"]);
        Assert.Contains("- Coke 330ml: 3 cases (asked for 4) at R245.00 a case", message.Values["summary"]);
        Assert.Contains("- Fanta 330ml: not available", message.Values["summary"]);
    }

    [Fact]
    public async Task A_supplier_that_declines_everything_cancels_the_order_and_says_so()
    {
        var order = await SentToApp();
        var decline = order.Lines.Select(l => new LineDecisionRequest(l.Id, "decline", null, null)).ToList();

        var reply = await Respond().Handle(new RespondToOrder(order.Id, new(_supplierPerson, decline, null)), default);

        Assert.Equal("cancelled", reply.Value!.Status);
        Assert.Contains("could not supply any of it", _queue.Sent.Single().Values["summary"]);
    }

    [Fact]
    public async Task Only_that_supplier_can_reply_and_a_bad_reply_changes_nothing_and_tells_no_one()
    {
        var order = await SentToApp();

        Assert.Equal("permission.denied", (await Respond().Handle(new RespondToOrder(order.Id, new(Guid.NewGuid(), ConfirmAll(order), new DateOnly(2026, 10, 28))), default)).Error!.Code);
        Assert.Equal("order.delivery_date_invalid", (await Respond().Handle(new RespondToOrder(order.Id, new(_supplierPerson, ConfirmAll(order), new DateOnly(2026, 10, 26))), default)).Error!.Code);
        Assert.Equal("order.not_found", (await Respond().Handle(new RespondToOrder(Guid.NewGuid(), new(_supplierPerson, [], null)), default)).Error!.Code);
        Assert.Equal(OrderStatus.Sent, _orders.Orders.Single().Status);
        Assert.Empty(_queue.Sent);
    }

    // ---- deliver and receive ----

    private async Task<OrderResponse> Confirmed(params OrderLineRequest[] lines)
    {
        var order = await SentToApp(lines);
        return (await Respond().Handle(new RespondToOrder(order.Id, new(_supplierPerson, ConfirmAll(order), new DateOnly(2026, 10, 28))), default)).Value!;
    }

    [Fact]
    public async Task Receiving_a_delivery_remembers_the_price_makes_the_supplier_usual_and_announces_what_arrived()
    {
        var order = await Confirmed(new OrderLineRequest(_coke.Id, null, null, 4, null, null), new OrderLineRequest(_fanta.Id, null, null, 2, null, null));
        var coke = order.Lines[0];
        var fanta = order.Lines[1];

        var result = await Delivery().Handle(new ReceiveOrder(order.Id, new(_owner, new Dictionary<Guid, int> { [fanta.Id] = 1 })), default);   // only 1 of 2 Fanta cases arrived

        Assert.Equal("delivered", result.Value!.Status);
        Assert.Equal(4 * 24000 + 22000 + 3300 + 5000, result.Value.TotalCents);   // the missing case is not charged
        Assert.Equal([24000L, 22000L], _orders.Prices.OrderByDescending(p => p.CasePriceCents).Select(p => p.CasePriceCents));
        Assert.Single(_shopMarketplace.Usual);

        var announced = Assert.IsType<OrderReceived>(Assert.Single(_orders.Orders.Single().DomainEvents));
        Assert.Equal(("Fresh Foods", _orders.Orders.Single().Reference), (announced.SupplierName, announced.Reference));
        Assert.Equal((96, 1000L), (announced.Lines[0].Units, announced.Lines[0].UnitCostCents));         // 4 cases x 24, R240 / 24 = R10.00 (VAT included)
        Assert.Equal((24, 1054L), (announced.Lines[1].Units, announced.Lines[1].UnitCostCents));         // 1 case, R220 + 15% = R253 / 24 = R10.54
        Assert.Equal("6001111111111", announced.Lines[0].Barcode);
        _ = coke;
    }

    [Fact]
    public async Task A_second_order_updates_the_last_price_instead_of_adding_another()
    {
        var first = await Confirmed();
        await Delivery().Handle(new ReceiveOrder(first.Id, new(_owner, null)), default);
        _coke.Update(new SupplierProductInput("6001111111111", "Coke 330ml", 24, 25000, true, true, null), null);

        var second = await Confirmed();
        await Delivery().Handle(new ReceiveOrder(second.Id, new(_owner, null)), default);

        Assert.Equal(25000, Assert.Single(_orders.Prices).CasePriceCents);
        Assert.Single(_shopMarketplace.Usual);
    }

    [Fact]
    public async Task The_supplier_can_mark_it_delivered_and_the_shop_can_then_receive_it()
    {
        var order = await Confirmed();

        var delivered = await Delivery().Handle(new DeliverOrder(order.Id, new(_supplierPerson, new Dictionary<Guid, int> { [order.Lines[0].Id] = 3 })), default);

        Assert.Equal(("delivered", 3), (delivered.Value!.Status, delivered.Value.Lines[0].EffectiveCases));
        Assert.Equal("permission.denied", (await Delivery().Handle(new DeliverOrder(order.Id, new(Guid.NewGuid(), null)), default)).Error!.Code);
        Assert.True((await Delivery().Handle(new ReceiveOrder(order.Id, new(_owner, null)), default)).IsSuccess);
        Assert.Equal("order.already_received", (await Delivery().Handle(new ReceiveOrder(order.Id, new(_owner, null)), default)).Error!.Code);
    }

    [Fact]
    public async Task Receiving_needs_the_shops_permission_and_a_confirmed_order_and_counts_cannot_exceed_what_was_confirmed()
    {
        var sent = await SentToApp();
        Assert.Equal("order.not_delivered", (await Delivery().Handle(new ReceiveOrder(sent.Id, new(_owner, null)), default)).Error!.Code);

        var confirmed = await Confirmed();
        Assert.Equal("permission.denied", (await Delivery().Handle(new ReceiveOrder(confirmed.Id, new(Guid.NewGuid(), null)), default)).Error!.Code);
        Assert.Equal("order.delivered_invalid", (await Delivery().Handle(new ReceiveOrder(confirmed.Id, new(_owner, new Dictionary<Guid, int> { [confirmed.Lines[0].Id] = 99 })), default)).Error!.Code);
        Assert.Equal("order.not_found", (await Delivery().Handle(new ReceiveOrder(Guid.NewGuid(), new(_owner, null)), default)).Error!.Code);
        Assert.Empty(_orders.Prices);
    }

    // ---- reading ----

    [Fact]
    public async Task A_draft_is_private_to_the_shop_and_a_sent_order_is_visible_to_both_sides_but_no_one_else()
    {
        var draft = await Drafted();
        Assert.True((await Queries().Handle(new GetOrder(draft.Id, _owner), default)).IsSuccess);
        Assert.Equal("permission.denied", (await Queries().Handle(new GetOrder(draft.Id, _supplierPerson), default)).Error!.Code);

        await Send().Handle(new SendOrder(draft.Id, new(_owner, "app")), default);
        Assert.True((await Queries().Handle(new GetOrder(draft.Id, _supplierPerson), default)).IsSuccess);
        Assert.Equal("permission.denied", (await Queries().Handle(new GetOrder(draft.Id, Guid.NewGuid()), default)).Error!.Code);
        Assert.Equal("order.not_found", (await Queries().Handle(new GetOrder(Guid.NewGuid(), _owner), default)).Error!.Code);
    }

    [Fact]
    public async Task The_suppliers_inbox_hides_drafts_and_filters_by_status_and_the_shop_sees_all_its_orders_newest_first()
    {
        var draft = await Drafted();
        var sent = await SentToApp();
        var confirmed = await Confirmed();

        var inbox = (await Queries().Handle(new ListSupplierOrders(_supplier.Id, _supplierPerson, null), default)).Value!;
        var onlyConfirmed = (await Queries().Handle(new ListSupplierOrders(_supplier.Id, _supplierPerson, "confirmed"), default)).Value!;
        var shopSide = (await Queries().Handle(new ListShopOrders(_shop, _owner), default)).Value!;

        Assert.DoesNotContain(draft.Id, inbox.Select(o => o.Id));
        Assert.Contains(sent.Id, inbox.Select(o => o.Id));
        Assert.Equal([confirmed.Id], onlyConfirmed.Select(o => o.Id));
        Assert.Equal(3, shopSide.Count);
        Assert.Equal("order.status_invalid", (await Queries().Handle(new ListSupplierOrders(_supplier.Id, _supplierPerson, "bogus"), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Queries().Handle(new ListSupplierOrders(_supplier.Id, Guid.NewGuid(), null), default)).Error!.Code);
        Assert.Equal("permission.denied", (await Queries().Handle(new ListShopOrders(_shop, Guid.NewGuid()), default)).Error!.Code);
    }
}
