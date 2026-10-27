using Identity.Contracts;
using Microsoft.Extensions.Time.Testing;
using ShopCore.Application.Selling;
using ShopCore.Application.Sync;
using ShopCore.Domain;
using ShopCore.Domain.Selling;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Tests;

internal sealed class FakeSellingStore : ISellingStore
{
    public List<Product> Products { get; } = [];
    public List<ModifierGroup> GroupRows { get; } = [];
    public Dictionary<Guid, List<Guid>> ProductGroups { get; } = [];
    public List<Promotion> PromotionRows { get; } = [];
    public List<FloorTable> TableRows { get; } = [];
    public List<OpenTicket> TicketRows { get; } = [];
    public List<KitchenOrder> KitchenRows { get; } = [];
    public List<Quote> QuoteRows { get; } = [];
    public List<LaybyPlan> LaybyRows { get; } = [];
    public List<GiftVoucher> VoucherRows { get; } = [];
    public List<SerialNumberRecord> SerialRows { get; } = [];
    public Dictionary<Guid, Dictionary<Guid, int>> Sold { get; } = [];
    public Dictionary<Guid, SaleSummary> Summaries { get; } = [];
    public HashSet<Guid> Cancelled { get; } = [];
    public int Saves { get; private set; }

    public Task<Product?> GetProduct(Guid shopId, Guid productId, CancellationToken ct) => Task.FromResult(Products.FirstOrDefault(p => p.ShopId == shopId && p.Id == productId));
    public Task<List<Product>> ProductsByIds(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult(Products.Where(p => p.ShopId == shopId && ids.Contains(p.Id)).ToList());
    public Task<bool> BarcodeTaken(Guid shopId, string barcode, CancellationToken ct) => Task.FromResult(Products.Any(p => p.ShopId == shopId && p.Barcode == barcode));
    public Task<List<Product>> VariantsOf(Guid parentProductId, CancellationToken ct) => Task.FromResult(Products.Where(p => p.ParentProductId == parentProductId).ToList());
    public void Add(Product product) => Products.Add(product);

    public void Add(ModifierGroup group) => GroupRows.Add(group);
    public Task<ModifierGroup?> GetGroup(Guid shopId, Guid groupId, CancellationToken ct) => Task.FromResult(GroupRows.FirstOrDefault(g => g.ShopId == shopId && g.Id == groupId));
    public Task<List<ModifierGroup>> Groups(Guid shopId, CancellationToken ct) => Task.FromResult(GroupRows.Where(g => g.ShopId == shopId).ToList());
    public Task<List<ModifierGroup>> GroupsByIds(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult(GroupRows.Where(g => g.ShopId == shopId && ids.Contains(g.Id)).ToList());
    public Task<List<Guid>> GroupIdsOfProduct(Guid productId, CancellationToken ct) => Task.FromResult(ProductGroups.GetValueOrDefault(productId) ?? []);
    public Task ReplaceProductGroups(Guid productId, IReadOnlyList<Guid> groupIds, CancellationToken ct) { ProductGroups[productId] = [.. groupIds]; return Task.CompletedTask; }

    public void Add(Promotion promotion) => PromotionRows.Add(promotion);
    public Task<Promotion?> GetPromotion(Guid shopId, Guid promotionId, CancellationToken ct) => Task.FromResult(PromotionRows.FirstOrDefault(p => p.ShopId == shopId && p.Id == promotionId));
    public Task<List<Promotion>> Promotions(Guid shopId, CancellationToken ct) => Task.FromResult(PromotionRows.Where(p => p.ShopId == shopId).ToList());
    public Task<List<FloorTable>> Tables(Guid shopId, CancellationToken ct) => Task.FromResult(TableRows.Where(t => t.ShopId == shopId).ToList());
    public void Add(FloorTable table) => TableRows.Add(table);

    public void Add(OpenTicket ticket) => TicketRows.Add(ticket);
    public Task<OpenTicket?> GetTicket(Guid shopId, Guid ticketId, CancellationToken ct) => Task.FromResult(TicketRows.FirstOrDefault(t => t.ShopId == shopId && t.Id == ticketId));
    public Task<List<OpenTicket>> OpenTickets(Guid shopId, CancellationToken ct) => Task.FromResult(TicketRows.Where(t => t.ShopId == shopId && t.Status == TicketStatus.Open).ToList());
    public void Add(KitchenOrder order) => KitchenRows.Add(order);
    public Task<KitchenOrder?> GetKitchenOrder(Guid shopId, Guid orderId, CancellationToken ct) => Task.FromResult(KitchenRows.FirstOrDefault(o => o.ShopId == shopId && o.Id == orderId));
    public Task<KitchenOrder?> KitchenOrderByNo(Guid shopId, string orderNo, CancellationToken ct) => Task.FromResult(KitchenRows.FirstOrDefault(o => o.ShopId == shopId && o.OrderNo == orderNo));
    public Task<List<KitchenOrder>> KitchenOrders(Guid shopId, IReadOnlyCollection<KitchenStatus> statuses, DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult(KitchenRows.Where(o => o.ShopId == shopId && statuses.Contains(o.Status) && o.CreatedAt >= since).ToList());

    public void Add(Quote quote) => QuoteRows.Add(quote);
    public Task<Quote?> GetQuote(Guid shopId, Guid quoteId, CancellationToken ct) => Task.FromResult(QuoteRows.FirstOrDefault(q => q.ShopId == shopId && q.Id == quoteId));
    public Task<List<Quote>> Quotes(Guid shopId, CancellationToken ct) => Task.FromResult(QuoteRows.Where(q => q.ShopId == shopId).ToList());
    public void Add(LaybyPlan plan) => LaybyRows.Add(plan);
    public Task<LaybyPlan?> GetLayby(Guid shopId, Guid planId, CancellationToken ct) => Task.FromResult(LaybyRows.FirstOrDefault(p => p.ShopId == shopId && p.Id == planId));
    public Task<List<LaybyPlan>> Laybys(Guid shopId, CancellationToken ct) => Task.FromResult(LaybyRows.Where(p => p.ShopId == shopId).ToList());
    public Task<List<LaybyPlan>> ActiveLaybysPastDue(DateOnly today, int take, CancellationToken ct) => Task.FromResult(LaybyRows.Where(p => p.Status == LaybyStatus.Active && p.DueOn < today).Take(take).ToList());

    public void Add(GiftVoucher voucher) => VoucherRows.Add(voucher);
    public Task<GiftVoucher?> VoucherByHash(Guid shopId, string codeHash, CancellationToken ct) => Task.FromResult(VoucherRows.FirstOrDefault(v => v.ShopId == shopId && v.CodeHash == codeHash));
    public void Add(SerialNumberRecord serial) => SerialRows.Add(serial);
    public Task<bool> SerialExists(Guid shopId, Guid productId, string serial, CancellationToken ct) => Task.FromResult(SerialRows.Any(s => s.ShopId == shopId && s.ProductId == productId && s.Serial == serial));
    public Task<List<SerialNumberRecord>> FindSerials(Guid shopId, string text, int take, CancellationToken ct) =>
        Task.FromResult(SerialRows.Where(s => s.ShopId == shopId && s.Serial.Contains(text.ToUpperInvariant())).Take(take).ToList());
    public Task<Dictionary<Guid, int>> SoldQuantities(Guid shopId, Guid saleId, CancellationToken ct) => Task.FromResult(Sold.GetValueOrDefault(saleId) ?? []);
    public Task<Dictionary<Guid, int>> SerialCounts(Guid shopId, Guid saleId, CancellationToken ct) =>
        Task.FromResult(SerialRows.Where(s => s.SaleId == saleId).GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.Count()));

    public Task<SaleSummary?> SaleSummary(Guid shopId, Guid saleId, CancellationToken ct) => Task.FromResult(Summaries.TryGetValue(saleId, out var s) ? s : null);
    public Task<bool> SaleCancelled(Guid saleId, CancellationToken ct) => Task.FromResult(Cancelled.Contains(saleId));
    public Task Save(CancellationToken ct) { Saves++; return Task.CompletedTask; }
}

internal sealed class WorldGate : IFeatureGate
{
    public HashSet<string> Off { get; } = [];
    public Dictionary<string, string> SettingsByFeature { get; } = [];
    public Task<bool> IsOn(Guid shopId, string feature, CancellationToken ct) => Task.FromResult(!Off.Contains(feature));
    public Task<string?> Settings(Guid shopId, string feature, CancellationToken ct) => Task.FromResult(SettingsByFeature.GetValueOrDefault(feature));
}

internal sealed class WorldTills : ITillAccessChecker
{
    public HashSet<(Guid Device, Guid Staff)> Allowed { get; } = [];
    public HashSet<(Guid Staff, string Permission)> Denied { get; } = [];
    public Task<Result> Check(Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct) =>
        Task.FromResult(!Allowed.Contains((deviceId, staffId)) ? Result.Fail("device.not_approved", "This till is not approved.")
            : Denied.Contains((staffId, permission)) ? Result.Fail("permission.denied", "No.") : Result.Ok());
}

internal sealed class WorldPeople : IOrganisationAccess
{
    public HashSet<(Guid Person, string Permission)> Granted { get; } = [];
    public Task<bool> Has(Guid personId, Guid organisationId, string permission, CancellationToken ct) => Task.FromResult(Granted.Contains((personId, permission)));
}

/// <summary>A shop with an owner, a cashier on two tills, and every Phase 4 handler ready to call.</summary>
internal sealed class SellingWorld
{
    public static readonly DateTimeOffset Start = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);   // Monday, midday in South Africa

    public FakeTimeProvider Clock { get; } = new(Start);
    public FakeSellingStore Store { get; } = new();
    public WorldGate Gate { get; } = new();
    public WorldTills Tills { get; } = new();
    public WorldPeople People { get; } = new();
    public Guid Shop { get; } = Guid.NewGuid();
    public Guid Owner { get; } = Guid.NewGuid();
    public Guid Cashier { get; } = Guid.NewGuid();
    public Guid Manager { get; } = Guid.NewGuid();
    public Guid TillA { get; } = Guid.NewGuid();
    public Guid TillB { get; } = Guid.NewGuid();

    public SellingWorld()
    {
        foreach (var till in new[] { TillA, TillB })
        {
            Tills.Allowed.Add((till, Cashier));
            Tills.Allowed.Add((till, Manager));
        }
        People.Granted.Add((Owner, "product.edit"));
        People.Granted.Add((Owner, "shop.settings"));
    }

    public VariantHandlers Variants => new(Store, People, Gate, Clock);
    public ModifierHandlers Modifiers => new(Store, People, Tills, Gate, Clock);
    public PromotionHandlers Promotions => new(Store, People, Tills, Gate, Clock);
    public FloorPlanHandlers Floor => new(Store, People, Tills, Gate, Clock);
    public TicketHandlers Tickets => new(Store, Tills, Gate, Clock);
    public KitchenHandlers Kitchen => new(Store, Tills, Gate, Clock);
    public QuoteHandlers Quotes => new(Store, Tills, Gate, Clock);
    public LaybyHandlers Laybys => new(Store, Tills, Gate, Clock);
    public VoucherHandlers Vouchers => new(Store, Tills, Gate, Clock);
    public SerialHandlers Serials => new(Store, Tills, Gate, Clock);

    public Product NewProduct(string name = "Burger", long price = 4_500, string? barcode = null, string category = "groceries")
    {
        var product = Product.Record(new ProductInput(Guid.NewGuid(), Shop, Cashier, TillA, null, name, barcode, category, price / 2, price, 3, null, true, Start));
        Store.Products.Add(product);
        return product;
    }
}
