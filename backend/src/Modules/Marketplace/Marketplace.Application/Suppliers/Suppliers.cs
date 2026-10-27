using Catalogue.Contracts;
using Identity.Contracts;
using Marketplace.Contracts;
using Marketplace.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Marketplace.Application.Suppliers;

public interface ISupplierStore
{
    Task Add(Supplier supplier, CancellationToken ct);
    Task<Supplier?> Get(Guid supplierId, CancellationToken ct);
    Task<List<Supplier>> Pending(CancellationToken ct);
    Task<List<SupplierArea>> Areas(Guid supplierId, CancellationToken ct);
    Task ReplaceAreas(Guid supplierId, List<SupplierArea> areas, CancellationToken ct);
    Task Save(CancellationToken ct);
}

internal static class SupplierAccess
{
    /// <summary>The caller must hold the permission in the supplier's own organisation. Platform admins use their own permissions instead.</summary>
    public static async Task<Result> Check(IOrganisationAccess access, Supplier supplier, Guid callerPersonId, string permission, CancellationToken ct) =>
        supplier.OrganisationId is { } org && await access.Has(callerPersonId, org, permission, ct)
            ? Result.Ok()
            : Result.Fail("permission.denied", "You do not have access to this supplier.");
}

internal static class AreaParsing
{
    public static async Task<Result<List<(string Code, DeliveryDays Days)>>> Parse(IEnumerable<AreaRequest> areas, ILookupChecker lookups, CancellationToken ct)
    {
        var parsed = new List<(string, DeliveryDays)>();
        foreach (var a in areas)
        {
            if (!await lookups.IsActive("municipalities", a.MunicipalityCode, ct))
                return Result<List<(string, DeliveryDays)>>.Fail("supplier.area_invalid", $"'{a.MunicipalityCode}' is not a municipality we know.");
            if (!SupplierArea.TryParseDays(a.Days ?? [], out var days))
                return Result<List<(string, DeliveryDays)>>.Fail("supplier.days_invalid", "Days are Mon, Tue, Wed, Thu, Fri, Sat and Sun.");
            parsed.Add((a.MunicipalityCode, days));
        }
        return Result<List<(string, DeliveryDays)>>.Ok(parsed);
    }
}

// ---- M1-1: apply ----

public sealed record ApplyAsSupplier(ApplyAsSupplierRequest Request) : ICommand<SupplierApplicationResponse>;

internal sealed class ApplyAsSupplierHandler(ISupplierStore store, ISupplierOrganisations organisations, ILookupChecker lookups, TimeProvider clock)
    : ICommandHandler<ApplyAsSupplier, SupplierApplicationResponse>
{
    public async Task<Result<SupplierApplicationResponse>> Handle(ApplyAsSupplier c, CancellationToken ct)
    {
        var r = c.Request;
        var areas = await AreaParsing.Parse(r.Areas ?? [], lookups, ct);
        if (!areas.IsSuccess) return Result<SupplierApplicationResponse>.Fail(areas.Error!.Code, areas.Error.Message);

        // Validate everything before creating the organisation, so a bad form leaves nothing behind.
        var placeholder = Supplier.Apply(new SupplierApplication(r.ApplicantPersonId, Guid.Empty, r.Name, r.RegistrationNo, r.ContactName, r.Phone, r.Whatsapp, r.Email, areas.Value!.Count), clock.GetUtcNow());
        if (!placeholder.IsSuccess) return Result<SupplierApplicationResponse>.Fail(placeholder.Error!.Code, placeholder.Error.Message);

        var organisation = await organisations.Create(r.Name, r.ApplicantPersonId, ct);
        if (!organisation.IsSuccess) return Result<SupplierApplicationResponse>.Fail(organisation.Error!.Code, organisation.Error.Message);

        var supplier = Supplier.Apply(new SupplierApplication(r.ApplicantPersonId, organisation.Value, r.Name, r.RegistrationNo, r.ContactName, r.Phone, r.Whatsapp, r.Email, areas.Value.Count), clock.GetUtcNow()).Value!;
        await store.Add(supplier, ct);
        await store.ReplaceAreas(supplier.Id, SupplierArea.Build(supplier.Id, areas.Value).Value!, ct);
        await store.Save(ct);
        return Result<SupplierApplicationResponse>.Ok(new SupplierApplicationResponse(supplier.Id, organisation.Value, "pending"));
    }
}

// ---- M1-2: verify or reject ----

public sealed record VerifySupplier(Guid SupplierId, VerifySupplierRequest Request) : ICommand<Unit>;

public sealed record RejectSupplier(Guid SupplierId, RejectSupplierRequest Request) : ICommand<Unit>;

public sealed record ListPendingSuppliers(Guid AdminPersonId) : IQuery<List<SupplierResponse>>;

internal sealed class SupplierReviewHandlers(ISupplierStore store, IPermissionChecker permissions, ISupplierOrganisations organisations, TimeProvider clock)
    : ICommandHandler<VerifySupplier, Unit>, ICommandHandler<RejectSupplier, Unit>, IQueryHandler<ListPendingSuppliers, List<SupplierResponse>>
{
    public async Task<Result<Unit>> Handle(VerifySupplier c, CancellationToken ct)
    {
        if (!await permissions.Has(c.Request.AdminPersonId, "supplier.verify", ct))
            return Result<Unit>.Fail("permission.denied", "Only a platform admin can verify suppliers.");
        var supplier = await store.Get(c.SupplierId, ct);
        if (supplier is null) return Result<Unit>.Fail("supplier.not_found", "Supplier not found.");

        var verified = supplier.Verify(c.Request.AdminPersonId, c.Request.Checklist ?? [], clock.GetUtcNow());
        if (!verified.IsSuccess) return Result<Unit>.Fail(verified.Error!.Code, verified.Error.Message);

        if (supplier.OrganisationId is { } org)
        {
            var status = await organisations.SetStatus(org, "verified", ct);
            if (!status.IsSuccess) return Result<Unit>.Fail(status.Error!.Code, status.Error.Message);
        }
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<Unit>> Handle(RejectSupplier c, CancellationToken ct)
    {
        if (!await permissions.Has(c.Request.AdminPersonId, "supplier.verify", ct))
            return Result<Unit>.Fail("permission.denied", "Only a platform admin can reject suppliers.");
        var supplier = await store.Get(c.SupplierId, ct);
        if (supplier is null) return Result<Unit>.Fail("supplier.not_found", "Supplier not found.");

        var rejected = supplier.Reject(c.Request.AdminPersonId, c.Request.Reason, clock.GetUtcNow());
        if (!rejected.IsSuccess) return Result<Unit>.Fail(rejected.Error!.Code, rejected.Error.Message);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<List<SupplierResponse>>> Handle(ListPendingSuppliers q, CancellationToken ct)
    {
        if (!await permissions.Has(q.AdminPersonId, "supplier.verify", ct))
            return Result<List<SupplierResponse>>.Fail("permission.denied", "Only a platform admin can see applications.");
        var list = new List<SupplierResponse>();
        foreach (var s in (await store.Pending(ct)).OrderBy(s => s.CreatedAt))
            list.Add(SupplierMapping.ToResponse(s, await store.Areas(s.Id, ct)));
        return Result<List<SupplierResponse>>.Ok(list);
    }
}

internal static class SupplierMapping
{
    public static SupplierResponse ToResponse(Supplier s, IEnumerable<SupplierArea> areas) => new(
        s.Id, s.Name, s.Status.ToString().ToLowerInvariant(), s.RejectionReason, s.MinOrderCents, s.DeliveryFeeCents,
        s.PaymentTerms switch { PaymentTerms.OnDelivery => "on_delivery", PaymentTerms.InApp => "in_app", _ => "both" },
        areas.OrderBy(a => a.MunicipalityCode).Select(a => new AreaResponse(a.MunicipalityCode, [.. SupplierArea.DayNames(a.Days)])).ToList(),
        s.IsTrading);
}

public sealed record GetSupplier(Guid SupplierId, Guid CallerPersonId) : IQuery<SupplierResponse>;

internal sealed class GetSupplierHandler(ISupplierStore store, IOrganisationAccess access) : IQueryHandler<GetSupplier, SupplierResponse>
{
    public async Task<Result<SupplierResponse>> Handle(GetSupplier q, CancellationToken ct)
    {
        var supplier = await store.Get(q.SupplierId, ct);
        if (supplier is null) return Result<SupplierResponse>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, q.CallerPersonId, "supplier.catalogue", ct);
        if (!allowed.IsSuccess) return Result<SupplierResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return Result<SupplierResponse>.Ok(SupplierMapping.ToResponse(supplier, await store.Areas(supplier.Id, ct)));
    }
}

// ---- M1-3: reps ----

public sealed record InviteRep(Guid SupplierId, InviteRepRequest Request) : ICommand<Guid>;

internal sealed class InviteRepHandler(ISupplierStore store, IOrganisationAccess access, ISupplierOrganisations organisations) : ICommandHandler<InviteRep, Guid>
{
    public async Task<Result<Guid>> Handle(InviteRep c, CancellationToken ct)
    {
        var supplier = await store.Get(c.SupplierId, ct);
        if (supplier is null) return Result<Guid>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, c.Request.CallerPersonId, "supplier.reps", ct);
        if (!allowed.IsSuccess) return Result<Guid>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return await organisations.AddRep(supplier.OrganisationId!.Value, c.Request.Name, c.Request.Phone, ct);
    }
}

// ---- M2-2: areas and terms ----

public sealed record SetSupplierAreas(Guid SupplierId, SetAreasRequest Request) : ICommand<Unit>;

public sealed record SetSupplierTerms(Guid SupplierId, SetTermsRequest Request) : ICommand<Unit>;

internal sealed class SupplierSettingsHandlers(ISupplierStore store, IOrganisationAccess access, ILookupChecker lookups)
    : ICommandHandler<SetSupplierAreas, Unit>, ICommandHandler<SetSupplierTerms, Unit>
{
    public async Task<Result<Unit>> Handle(SetSupplierAreas c, CancellationToken ct)
    {
        var supplier = await store.Get(c.SupplierId, ct);
        if (supplier is null) return Result<Unit>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, c.Request.CallerPersonId, "supplier.manage", ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);

        var parsed = await AreaParsing.Parse(c.Request.Areas ?? [], lookups, ct);
        if (!parsed.IsSuccess) return Result<Unit>.Fail(parsed.Error!.Code, parsed.Error.Message);
        var areas = SupplierArea.Build(supplier.Id, parsed.Value!);
        if (!areas.IsSuccess) return Result<Unit>.Fail(areas.Error!.Code, areas.Error.Message);

        await store.ReplaceAreas(supplier.Id, areas.Value!, ct);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<Unit>> Handle(SetSupplierTerms c, CancellationToken ct)
    {
        var supplier = await store.Get(c.SupplierId, ct);
        if (supplier is null) return Result<Unit>.Fail("supplier.not_found", "Supplier not found.");
        var allowed = await SupplierAccess.Check(access, supplier, c.Request.CallerPersonId, "supplier.manage", ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);

        PaymentTerms? terms = c.Request.PaymentTerms switch { "on_delivery" => PaymentTerms.OnDelivery, "in_app" => PaymentTerms.InApp, "both" => PaymentTerms.Both, _ => null };
        if (terms is null) return Result<Unit>.Fail("supplier.terms_invalid", "Payment terms are on_delivery, in_app or both.");
        var set = supplier.SetTerms(c.Request.MinOrderCents, c.Request.DeliveryFeeCents, terms.Value);
        if (!set.IsSuccess) return Result<Unit>.Fail(set.Error!.Code, set.Error.Message);

        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}
