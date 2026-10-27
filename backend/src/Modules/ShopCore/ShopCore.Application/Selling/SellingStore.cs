using Identity.Contracts;
using ShopCore.Application.Sync;
using ShopCore.Domain;
using ShopCore.Domain.Selling;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Selling;

/// <summary>Everything the Phase 4 selling features keep. One interface so one fake stands in for the database in tests.</summary>
public interface ISellingStore
{
    // products and variants
    Task<Product?> GetProduct(Guid shopId, Guid productId, CancellationToken ct);
    Task<List<Product>> ProductsByIds(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<bool> BarcodeTaken(Guid shopId, string barcode, CancellationToken ct);
    Task<List<Product>> VariantsOf(Guid parentProductId, CancellationToken ct);
    void Add(Product product);

    // modifiers
    void Add(ModifierGroup group);
    Task<ModifierGroup?> GetGroup(Guid shopId, Guid groupId, CancellationToken ct);
    Task<List<ModifierGroup>> Groups(Guid shopId, CancellationToken ct);
    Task<List<ModifierGroup>> GroupsByIds(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<List<Guid>> GroupIdsOfProduct(Guid productId, CancellationToken ct);
    Task ReplaceProductGroups(Guid productId, IReadOnlyList<Guid> groupIds, CancellationToken ct);

    // promotions and the floor plan
    void Add(Promotion promotion);
    Task<Promotion?> GetPromotion(Guid shopId, Guid promotionId, CancellationToken ct);
    Task<List<Promotion>> Promotions(Guid shopId, CancellationToken ct);
    Task<List<FloorTable>> Tables(Guid shopId, CancellationToken ct);
    void Add(FloorTable table);

    // tickets and the kitchen
    void Add(OpenTicket ticket);
    Task<OpenTicket?> GetTicket(Guid shopId, Guid ticketId, CancellationToken ct);
    Task<List<OpenTicket>> OpenTickets(Guid shopId, CancellationToken ct);
    void Add(KitchenOrder order);
    Task<KitchenOrder?> GetKitchenOrder(Guid shopId, Guid orderId, CancellationToken ct);
    Task<KitchenOrder?> KitchenOrderByNo(Guid shopId, string orderNo, CancellationToken ct);
    Task<List<KitchenOrder>> KitchenOrders(Guid shopId, IReadOnlyCollection<KitchenStatus> statuses, DateTimeOffset since, CancellationToken ct);

    // quotes and lay-by
    void Add(Quote quote);
    Task<Quote?> GetQuote(Guid shopId, Guid quoteId, CancellationToken ct);
    Task<List<Quote>> Quotes(Guid shopId, CancellationToken ct);
    void Add(LaybyPlan plan);
    Task<LaybyPlan?> GetLayby(Guid shopId, Guid planId, CancellationToken ct);
    Task<List<LaybyPlan>> Laybys(Guid shopId, CancellationToken ct);
    Task<List<LaybyPlan>> ActiveLaybysPastDue(DateOnly today, int take, CancellationToken ct);

    // vouchers and serials
    void Add(GiftVoucher voucher);
    Task<GiftVoucher?> VoucherByHash(Guid shopId, string codeHash, CancellationToken ct);
    void Add(SerialNumberRecord serial);
    Task<bool> SerialExists(Guid shopId, Guid productId, string serial, CancellationToken ct);
    Task<List<SerialNumberRecord>> FindSerials(Guid shopId, string text, int take, CancellationToken ct);
    Task<Dictionary<Guid, int>> SoldQuantities(Guid shopId, Guid saleId, CancellationToken ct);
    Task<Dictionary<Guid, int>> SerialCounts(Guid shopId, Guid saleId, CancellationToken ct);

    // online refunds
    Task<SaleSummary?> SaleSummary(Guid shopId, Guid saleId, CancellationToken ct);
    Task<bool> SaleCancelled(Guid saleId, CancellationToken ct);

    Task Save(CancellationToken ct);
}

/// <summary>Who may do something, and whether the shop's business pack allows it. Every handler starts with these two questions.</summary>
internal static class SellingAccess
{
    public static async Task<Result> Till(ITillAccessChecker tills, Guid shopId, Guid deviceId, Guid staffId, string permission, CancellationToken ct) =>
        await tills.Check(shopId, deviceId, staffId, permission, ct);

    public static async Task<Result> Person(IOrganisationAccess access, Guid personId, Guid shopId, string permission, CancellationToken ct) =>
        await access.Has(personId, shopId, permission, ct) ? Result.Ok() : Result.Fail("permission.denied", "You do not have access to this shop.");

    public static async Task<Result> Feature(IFeatureGate gate, Guid shopId, string feature, CancellationToken ct) =>
        await gate.IsOn(shopId, feature, ct) ? Result.Ok() : Result.Fail("feature.not_enabled", $"'{feature}' is not switched on for this shop.");

    /// <summary>The caller's access and the feature, in one go. Returns the first failure.</summary>
    public static async Task<Result> TillAndFeature(ITillAccessChecker tills, IFeatureGate gate, Guid shopId, Guid deviceId, Guid staffId, string permission, string feature, CancellationToken ct)
    {
        var access = await Till(tills, shopId, deviceId, staffId, permission, ct);
        return access.IsSuccess ? await Feature(gate, shopId, feature, ct) : access;
    }

    public static async Task<Result> PersonAndFeature(IOrganisationAccess access, IFeatureGate gate, Guid personId, Guid shopId, string permission, string feature, CancellationToken ct)
    {
        var allowed = await Person(access, personId, shopId, permission, ct);
        return allowed.IsSuccess ? await Feature(gate, shopId, feature, ct) : allowed;
    }
}
