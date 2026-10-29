using Identity.Contracts;
using Platform.Contracts;
using Platform.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Platform.Application;

public interface IPackStore
{
    Task Add(BusinessPack pack, CancellationToken ct);
    Task<BusinessPack?> Get(string code, CancellationToken ct);
    Task<List<BusinessPack>> List(bool activeOnly, CancellationToken ct);
    Task<ShopPack?> ShopPack(Guid shopId, CancellationToken ct);
    Task Add(ShopPack shopPack, CancellationToken ct);
    Task Add(ShopPackChange change, CancellationToken ct);
    Task<int> ShopsOn(string packCode, CancellationToken ct);
    Task Save(CancellationToken ct);
}

internal static class PackMapping
{
    public static PackResponse ToResponse(BusinessPack p) =>
        new(p.Id, p.Code, p.Name, p.Description, p.SlipTemplateCode, p.Version, p.IsActive, p.Features.Select(f => new FeatureDto(f.Flag, f.SettingsJson)).OrderBy(f => f.Flag).ToList());

    public static ShopPackResponse ToShopPack(Guid shopId, string shopName, BusinessPack p) =>
        new(shopId, p.Code, p.Name, p.Version, p.SlipTemplateCode, p.Features.Select(f => new FeatureDto(f.Flag, f.SettingsJson)).OrderBy(f => f.Flag).ToList(), shopName);
}

/// <summary>Answers "is this feature on?" for the rest of the system. Cheap enough to call on every request: one pack lookup.</summary>
public sealed class ShopFeatures(IPackStore store, IShopDirectory? shops = null) : IShopFeatures
{
    public async Task<bool> IsOn(Guid shopId, string feature, CancellationToken ct) => (await Resolve(shopId, ct)).Has(feature);

    /// <summary>The shop's name comes from Identity (IShopDirectory) so the till can print it on slips; empty when the directory does not know the shop.</summary>
    public async Task<ShopPackResponse> For(Guid shopId, CancellationToken ct)
    {
        var pack = await Resolve(shopId, ct);
        var info = shops is null ? null : await shops.Get(shopId, ct);
        return PackMapping.ToShopPack(shopId, info?.Name ?? "", pack);
    }

    private async Task<BusinessPack> Resolve(Guid shopId, CancellationToken ct)
    {
        var chosen = await store.ShopPack(shopId, ct);
        var pack = await store.Get(chosen?.PackCode ?? ShopPack.DefaultCode, ct);
        // A pack that was retired still serves the shops already on it; only choosing it anew is refused.
        return pack ?? await store.Get(ShopPack.DefaultCode, ct) ?? throw new InvalidOperationException("The default business pack is missing.");
    }
}

// ---------------------------------------------------------------- B1-2: an owner chooses a business type

public sealed record ListBusinessPacks : IQuery<List<PackResponse>>;
public sealed record GetShopPack(Guid ShopId, Guid CallerPersonId) : IQuery<ShopPackResponse>;
public sealed record ChooseShopPack(Guid ShopId, ChoosePackRequest Request) : ICommand<ShopPackResponse>;

internal sealed class ShopPackHandlers(IPackStore store, IShopFeatures features, IOrganisationAccess access, TimeProvider clock)
    : IQueryHandler<ListBusinessPacks, List<PackResponse>>, IQueryHandler<GetShopPack, ShopPackResponse>, ICommandHandler<ChooseShopPack, ShopPackResponse>
{
    public async Task<Result<List<PackResponse>>> Handle(ListBusinessPacks q, CancellationToken ct) =>
        Result<List<PackResponse>>.Ok((await store.List(activeOnly: true, ct)).OrderBy(p => p.Name).Select(PackMapping.ToResponse).ToList());

    public async Task<Result<ShopPackResponse>> Handle(GetShopPack q, CancellationToken ct)
    {
        if (!await access.Has(q.CallerPersonId, q.ShopId, "sale.create", ct) && !await access.Has(q.CallerPersonId, q.ShopId, "shop.settings", ct))
            return Result<ShopPackResponse>.Fail("permission.denied", "You do not have access to this shop.");
        return Result<ShopPackResponse>.Ok(await features.For(q.ShopId, ct));
    }

    /// <summary>
    /// Switching changes what the shop sees and what the server allows, and nothing else: no product, sale or customer is touched,
    /// so switching back brings everything back exactly as it was.
    /// </summary>
    public async Task<Result<ShopPackResponse>> Handle(ChooseShopPack c, CancellationToken ct)
    {
        if (!await access.Has(c.Request.CallerPersonId, c.ShopId, "shop.settings", ct)) return Result<ShopPackResponse>.Fail("permission.denied", "Only the owner can change the business type.");
        var pack = await store.Get(c.Request.PackCode?.Trim() ?? "", ct);
        if (pack is null || !pack.IsActive) return Result<ShopPackResponse>.Fail("pack.not_found", "That business type is not available.");

        var now = clock.GetUtcNow();
        var current = await store.ShopPack(c.ShopId, ct);
        var from = current?.PackCode;
        if (from == pack.Code) return Result<ShopPackResponse>.Ok(await features.For(c.ShopId, ct));

        if (current is null) await store.Add(new ShopPack(c.ShopId, pack.Code, c.Request.CallerPersonId, now), ct);
        else current.Change(pack.Code, c.Request.CallerPersonId, now);
        await store.Add(new ShopPackChange(c.ShopId, from, pack.Code, c.Request.CallerPersonId, now), ct);
        await store.Save(ct);
        return Result<ShopPackResponse>.Ok(await features.For(c.ShopId, ct));
    }
}

// ---------------------------------------------------------------- B1-1: the platform defines packs

public sealed record SavePack(string? Code, SavePackRequest Request) : ICommand<PackResponse>;
public sealed record SetPackActive(string Code, Guid CallerPersonId, bool Active) : ICommand<PackResponse>;
public sealed record ListAllBusinessPacks(Guid CallerPersonId) : IQuery<List<PackResponse>>;

internal sealed class PackAdminHandlers(IPackStore store, IPermissionChecker permissions, TimeProvider clock)
    : ICommandHandler<SavePack, PackResponse>, ICommandHandler<SetPackActive, PackResponse>, IQueryHandler<ListAllBusinessPacks, List<PackResponse>>
{
    /// <summary>Create when Code is null, edit when it names an existing pack (a new version).</summary>
    public async Task<Result<PackResponse>> Handle(SavePack c, CancellationToken ct)
    {
        var r = c.Request;
        if (!await permissions.Has(r.CallerPersonId, "pack.manage", ct)) return Result<PackResponse>.Fail("permission.denied", "Only a platform admin can manage business packs.");
        var input = new PackInput(c.Code ?? r.Code, r.Name, r.Description, r.SlipTemplateCode, (r.Features ?? []).Select(f => new FeatureSetting(f.Flag, f.SettingsJson)).ToList());

        if (c.Code is null)
        {
            if (await store.Get(input.Code.Trim(), ct) is not null) return Result<PackResponse>.Fail("pack.code_taken", "A pack with that code already exists.");
            var created = BusinessPack.Create(input, clock.GetUtcNow());
            if (!created.IsSuccess) return Result<PackResponse>.Fail(created.Error!.Code, created.Error.Message);
            await store.Add(created.Value!, ct);
            await store.Save(ct);
            return Result<PackResponse>.Ok(PackMapping.ToResponse(created.Value!));
        }

        var pack = await store.Get(c.Code, ct);
        if (pack is null) return Result<PackResponse>.Fail("pack.not_found", "Pack not found.");
        var edited = pack.Edit(input);
        if (!edited.IsSuccess) return Result<PackResponse>.Fail(edited.Error!.Code, edited.Error.Message);
        await store.Save(ct);
        return Result<PackResponse>.Ok(PackMapping.ToResponse(pack));
    }

    public async Task<Result<PackResponse>> Handle(SetPackActive c, CancellationToken ct)
    {
        if (!await permissions.Has(c.CallerPersonId, "pack.manage", ct)) return Result<PackResponse>.Fail("permission.denied", "Only a platform admin can manage business packs.");
        var pack = await store.Get(c.Code, ct);
        if (pack is null) return Result<PackResponse>.Fail("pack.not_found", "Pack not found.");
        // The default pack is where shops land, so it can never be retired.
        if (!c.Active && pack.Code == ShopPack.DefaultCode) return Result<PackResponse>.Fail("pack.default_required", "The default pack cannot be retired.");
        if (c.Active) pack.Activate(); else pack.Deactivate();
        await store.Save(ct);
        return Result<PackResponse>.Ok(PackMapping.ToResponse(pack));
    }

    public async Task<Result<List<PackResponse>>> Handle(ListAllBusinessPacks q, CancellationToken ct)
    {
        if (!await permissions.Has(q.CallerPersonId, "pack.manage", ct)) return Result<List<PackResponse>>.Fail("permission.denied", "Only a platform admin can manage business packs.");
        return Result<List<PackResponse>>.Ok((await store.List(activeOnly: false, ct)).OrderBy(p => p.Code).Select(PackMapping.ToResponse).ToList());
    }
}

/// <summary>The pack a till keeps, for an approved device of the shop. A till has no person to ask as, so the device is what proves it belongs here.</summary>
public sealed record GetDevicePack(Guid ShopId, Guid DeviceId) : IQuery<ShopPackResponse>;

internal sealed class DevicePackHandler(Identity.Contracts.IDeviceGate gate, IShopFeatures features) : IQueryHandler<GetDevicePack, ShopPackResponse>
{
    public async Task<Result<ShopPackResponse>> Handle(GetDevicePack q, CancellationToken ct)
    {
        var allowed = await gate.Approved(q.ShopId, q.DeviceId, ct);
        return allowed.IsSuccess ? Result<ShopPackResponse>.Ok(await features.For(q.ShopId, ct)) : Result<ShopPackResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
    }
}
