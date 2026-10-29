using Identity.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Identity.Application.Shops;

public interface IShopListStore
{
    Task<List<StaffListItem>> Staff(Guid shopId, CancellationToken ct);
    Task<List<DeviceListItem>> Devices(Guid shopId, CancellationToken ct);
}

public sealed record ListShopStaff(Guid ShopId, Guid CallerPersonId) : IQuery<List<StaffListItem>>;
public sealed record ListShopDevices(Guid ShopId, Guid CallerPersonId) : IQuery<List<DeviceListItem>>;

/// <summary>The owner's own view of who works at the shop and which tills it has. The staff id is the membership id the tills and the screens use.</summary>
internal sealed class ShopListHandlers(IShopListStore store, IOrganisationAccess access)
    : IQueryHandler<ListShopStaff, List<StaffListItem>>, IQueryHandler<ListShopDevices, List<DeviceListItem>>
{
    public async Task<Result<List<StaffListItem>>> Handle(ListShopStaff q, CancellationToken ct) =>
        await access.Has(q.CallerPersonId, q.ShopId, "staff.manage", ct)
            ? Result<List<StaffListItem>>.Ok((await store.Staff(q.ShopId, ct)).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList())
            : Result<List<StaffListItem>>.Fail("permission.denied", "Only the owner can see the staff.");

    public async Task<Result<List<DeviceListItem>>> Handle(ListShopDevices q, CancellationToken ct) =>
        await access.Has(q.CallerPersonId, q.ShopId, "device.manage", ct)
            ? Result<List<DeviceListItem>>.Ok((await store.Devices(q.ShopId, ct)).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList())
            : Result<List<DeviceListItem>>.Fail("permission.denied", "Only the owner can see the tills.");
}
