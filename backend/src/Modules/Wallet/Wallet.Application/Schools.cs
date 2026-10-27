using Identity.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application;

internal static class SchoolMapping
{
    public static SchoolResponse ToResponse(School s) => new(
        s.Id, s.Name, s.EmisNumber, s.ShopId, s.Status.ToString().ToLowerInvariant(), s.PayoutSchedule.ToString().ToLowerInvariant(), s.FeeBasisPoints,
        s.StaffAccountsEnabled, s.DailyCeilingCents, s.OfflineSplitMinutes);

    public static bool TryParseSchedule(string? text, out PayoutSchedule schedule)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "daily": schedule = PayoutSchedule.Daily; return true;
            case "weekly": schedule = PayoutSchedule.Weekly; return true;
            case "monthly": schedule = PayoutSchedule.Monthly; return true;
            default: schedule = default; return false;
        }
    }
}

// ---------------------------------------------------------------- W1-1: register and verify a school

public sealed record RegisterSchool(RegisterSchoolRequest Request) : ICommand<SchoolResponse>;
public sealed record VerifySchool(Guid SchoolId, Guid CallerPersonId) : ICommand<SchoolResponse>;
public sealed record ConfigureSchool(Guid SchoolId, ConfigureSchoolRequest Request) : ICommand<SchoolResponse>;
public sealed record AddSchoolAdmin(Guid SchoolId, AddSchoolAdminRequest Request) : ICommand<Unit>;
public sealed record GetSchool(Guid SchoolId, Guid CallerPersonId) : IQuery<SchoolResponse>;

internal sealed class SchoolHandlers(IWalletStore store, IShopDirectory shops, IPersonDirectory people, IPermissionChecker permissions, TimeProvider clock)
    : ICommandHandler<RegisterSchool, SchoolResponse>, ICommandHandler<VerifySchool, SchoolResponse>, ICommandHandler<ConfigureSchool, SchoolResponse>,
      ICommandHandler<AddSchoolAdmin, Unit>, IQueryHandler<GetSchool, SchoolResponse>
{
    public async Task<Result<SchoolResponse>> Handle(RegisterSchool c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await SchoolAccess.Platform(permissions, r.CallerPersonId, "school.manage", ct);
        if (!allowed.IsSuccess) return Result<SchoolResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var shop = await shops.Get(r.ShopId, ct);
        if (shop is null) return Result<SchoolResponse>.Fail("school.shop_not_found", "That tuckshop does not exist.");
        if (await store.SchoolByShop(r.ShopId, ct) is not null) return Result<SchoolResponse>.Fail("school.shop_taken", "That shop already belongs to a school.");

        var created = School.Register(r.Name, r.EmisNumber, r.ShopId, clock.GetUtcNow());
        if (!created.IsSuccess) return Result<SchoolResponse>.Fail(created.Error!.Code, created.Error.Message);

        await store.Add(created.Value!, ct);
        var payable = AccountIds.ShopPayable(r.ShopId);
        if (!await store.AccountExists(payable, ct)) await store.Add(new LedgerAccount(payable, LedgerAccountType.ShopPayable, r.ShopId, clock.GetUtcNow()), ct);
        await store.Add(new LedgerAccount(AccountIds.SchoolCash(created.Value!.Id), LedgerAccountType.SchoolCash, created.Value.Id, clock.GetUtcNow()), ct);
        await store.Save(ct);
        return Result<SchoolResponse>.Ok(SchoolMapping.ToResponse(created.Value));
    }

    public async Task<Result<SchoolResponse>> Handle(VerifySchool c, CancellationToken ct)
    {
        var allowed = await SchoolAccess.Platform(permissions, c.CallerPersonId, "school.manage", ct);
        if (!allowed.IsSuccess) return Result<SchoolResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var school = await store.GetSchool(c.SchoolId, ct);
        if (school is null) return Result<SchoolResponse>.Fail("school.not_found", "School not found.");

        // The tuckshop itself must be a verified shop before wallets can be loaded for it.
        var shop = await shops.Get(school.ShopId, ct);
        if (shop is null || shop.Status != "verified") return Result<SchoolResponse>.Fail("school.shop_not_verified", "The tuckshop has not been verified yet.");

        school.Verify(c.CallerPersonId, clock.GetUtcNow());
        await store.Save(ct);
        return Result<SchoolResponse>.Ok(SchoolMapping.ToResponse(school));
    }

    public async Task<Result<SchoolResponse>> Handle(ConfigureSchool c, CancellationToken ct)
    {
        var school = await store.GetSchool(c.SchoolId, ct);
        if (school is null) return Result<SchoolResponse>.Fail("school.not_found", "School not found.");
        // The fee is a commercial term, so only the platform sets it; a school admin cannot change what the platform keeps.
        var r = c.Request;
        var isPlatform = await permissions.Has(r.CallerPersonId, "school.manage", ct);
        if (!isPlatform)
        {
            var admin = await SchoolAccess.Admin(store, permissions, school.Id, r.CallerPersonId, ct);
            if (!admin.IsSuccess) return Result<SchoolResponse>.Fail(admin.Error!.Code, admin.Error.Message);
            if (r.FeeBasisPoints != school.FeeBasisPoints) return Result<SchoolResponse>.Fail("permission.denied", "Only the platform can change the fee.");
        }
        if (!SchoolMapping.TryParseSchedule(r.PayoutSchedule, out var schedule)) return Result<SchoolResponse>.Fail("school.schedule_invalid", "The payout schedule is daily, weekly or monthly.");

        var configured = school.Configure(r.FeeBasisPoints, schedule, r.StaffAccountsEnabled, r.DailyCeilingCents, r.OfflineSplitMinutes);
        if (!configured.IsSuccess) return Result<SchoolResponse>.Fail(configured.Error!.Code, configured.Error.Message);
        await store.Save(ct);
        return Result<SchoolResponse>.Ok(SchoolMapping.ToResponse(school));
    }

    public async Task<Result<Unit>> Handle(AddSchoolAdmin c, CancellationToken ct)
    {
        var allowed = await SchoolAccess.Platform(permissions, c.Request.CallerPersonId, "school.manage", ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (await store.GetSchool(c.SchoolId, ct) is null) return Result<Unit>.Fail("school.not_found", "School not found.");
        if (await people.Get(c.Request.PersonId, ct) is null) return Result<Unit>.Fail("school.person_not_found", "That person has not signed up yet.");
        if (await store.IsSchoolAdmin(c.SchoolId, c.Request.PersonId, ct)) return Result<Unit>.Ok(default);

        await store.Add(new SchoolAdmin(c.SchoolId, c.Request.PersonId, clock.GetUtcNow()), ct);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<SchoolResponse>> Handle(GetSchool q, CancellationToken ct)
    {
        var school = await store.GetSchool(q.SchoolId, ct);
        if (school is null) return Result<SchoolResponse>.Fail("school.not_found", "School not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, school.Id, q.CallerPersonId, ct);
        return allowed.IsSuccess ? Result<SchoolResponse>.Ok(SchoolMapping.ToResponse(school)) : Result<SchoolResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
    }
}
