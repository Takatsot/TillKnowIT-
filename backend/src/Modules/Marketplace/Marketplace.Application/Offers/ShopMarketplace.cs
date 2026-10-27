using Identity.Contracts;
using Marketplace.Application.Suppliers;
using Marketplace.Contracts;
using Marketplace.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Offers;

public interface IShopMarketplaceStore
{
    Task<SharingSetting?> GetSharing(Guid shopId, CancellationToken ct);
    Task Add(SharingSetting setting, CancellationToken ct);
    Task<ShopSupplier?> GetShopSupplier(Guid shopId, Guid supplierId, CancellationToken ct);
    Task Add(ShopSupplier shopSupplier, CancellationToken ct);
    Task<MarketplaceSetting?> GetSetting(string key, CancellationToken ct);
    Task Add(MarketplaceSetting setting, CancellationToken ct);
    Task<Dictionary<string, string>> Settings(CancellationToken ct);
    Task Save(CancellationToken ct);
}

// ---- M3-2: sharing ----

/// <summary>Off, usual suppliers only, or all suppliers in the shop's area. Off until the owner chooses.</summary>
public sealed record SetSharing(Guid ShopId, SetSharingRequest Request) : ICommand<SharingResponse>;

public sealed record GetSharing(Guid ShopId, Guid CallerPersonId) : IQuery<SharingResponse>;

internal sealed class SharingHandlers(IShopMarketplaceStore store, IOrganisationAccess access)
    : ICommandHandler<SetSharing, SharingResponse>, IQueryHandler<GetSharing, SharingResponse>
{
    public async Task<Result<SharingResponse>> Handle(SetSharing c, CancellationToken ct)
    {
        // Only the owner can decide who sees the shop's stock signals.
        if (!await access.Has(c.Request.CallerPersonId, c.ShopId, "shop.settings", ct))
            return Result<SharingResponse>.Fail("permission.denied", "Only the shop owner can change sharing.");
        if (!SharingModes.TryParse(c.Request.Mode, out var mode))
            return Result<SharingResponse>.Fail("sharing.mode_invalid", "Mode is off, usual or all.");

        var setting = await store.GetSharing(c.ShopId, ct);
        if (setting is null) await store.Add(new SharingSetting(c.ShopId, mode), ct);
        else setting.Change(mode);
        await store.Save(ct);
        return Result<SharingResponse>.Ok(new SharingResponse(mode.ToString().ToLowerInvariant()));
    }

    public async Task<Result<SharingResponse>> Handle(GetSharing q, CancellationToken ct)
    {
        if (!await access.Has(q.CallerPersonId, q.ShopId, "shop.settings", ct))
            return Result<SharingResponse>.Fail("permission.denied", "Only the shop owner can see sharing.");
        var setting = await store.GetSharing(q.ShopId, ct);
        return Result<SharingResponse>.Ok(new SharingResponse((setting?.Mode ?? SharingMode.Off).ToString().ToLowerInvariant()));
    }
}

// ---- usual supplier ----

public sealed record SetUsualSupplier(Guid ShopId, SetUsualSupplierRequest Request) : ICommand<Unit>;

internal sealed class SetUsualSupplierHandler(IShopMarketplaceStore store, ISupplierStore suppliers, IOrganisationAccess access) : ICommandHandler<SetUsualSupplier, Unit>
{
    public async Task<Result<Unit>> Handle(SetUsualSupplier c, CancellationToken ct)
    {
        if (!await access.Has(c.Request.CallerPersonId, c.ShopId, "shop.settings", ct))
            return Result<Unit>.Fail("permission.denied", "Only the shop owner can choose usual suppliers.");

        var supplier = await suppliers.Get(c.Request.SupplierId, ct);
        // A shop's usual supplier is one that trades on the platform, or one the shop added itself.
        var usable = supplier is not null && (supplier.IsTrading || (supplier.Status == SupplierStatus.Unlisted && supplier.CreatedByShopId == c.ShopId));
        if (!usable) return Result<Unit>.Fail("supplier.not_found", "Supplier not found.");

        if (await store.GetShopSupplier(c.ShopId, supplier!.Id, ct) is null)
        {
            await store.Add(new ShopSupplier(c.ShopId, supplier.Id), ct);
            await store.Save(ct);
        }
        return Result<Unit>.Ok(default);
    }
}

// ---- platform settings ----

public sealed record SaveMarketplaceSetting(string Key, SaveMarketplaceSettingRequest Request) : ICommand<Unit>;

public sealed record GetMarketplaceSettings(Guid AdminPersonId) : IQuery<Dictionary<string, string>>;

internal sealed class MarketplaceSettingsHandlers(IShopMarketplaceStore store, IPermissionChecker permissions)
    : ICommandHandler<SaveMarketplaceSetting, Unit>, IQueryHandler<GetMarketplaceSettings, Dictionary<string, string>>
{
    public async Task<Result<Unit>> Handle(SaveMarketplaceSetting c, CancellationToken ct)
    {
        if (!await permissions.Has(c.Request.AdminPersonId, "marketplace.settings", ct))
            return Result<Unit>.Fail("permission.denied", "Only a platform admin can change marketplace settings.");
        if (!RankingWeights.Keys.Contains(c.Key))
            return Result<Unit>.Fail("setting.key_invalid", $"Settings are: {string.Join(", ", RankingWeights.Keys)}.");
        if (!RankingWeights.IsValid(c.Key, c.Request.Value))
            return Result<Unit>.Fail("setting.value_invalid", "That value is not allowed for this setting.");

        var existing = await store.GetSetting(c.Key, ct);
        if (existing is null) await store.Add(new MarketplaceSetting(c.Key, c.Request.Value.Trim()), ct);
        else existing.Change(c.Request.Value.Trim());
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<Dictionary<string, string>>> Handle(GetMarketplaceSettings q, CancellationToken ct)
    {
        if (!await permissions.Has(q.AdminPersonId, "marketplace.settings", ct))
            return Result<Dictionary<string, string>>.Fail("permission.denied", "Only a platform admin can see marketplace settings.");
        return Result<Dictionary<string, string>>.Ok(await store.Settings(ct));
    }
}
