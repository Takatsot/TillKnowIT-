using Customers.Contracts;
using Customers.Domain;
using Identity.Contracts;
using Platform.Contracts;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Customers.Application;

public interface ICustomerStore
{
    void Add(Customer customer);
    Task<Customer?> Get(Guid shopId, Guid customerId, CancellationToken ct);
    Task<Customer?> ByPhone(Guid shopId, string phone, CancellationToken ct);
    Task<List<Customer>> Search(Guid shopId, string text, int take, CancellationToken ct);

    void Add(LoyaltyEntry entry);
    Task<LoyaltyEntry?> EntryByReference(Guid customerId, string reference, CancellationToken ct);
    Task<List<LoyaltyEntry>> EntriesForSale(Guid saleId, CancellationToken ct);
    Task<int> Balance(Guid customerId, CancellationToken ct);
    Task<Dictionary<Guid, int>> Balances(IReadOnlyCollection<Guid> customerIds, CancellationToken ct);
    Task<List<LoyaltyEntry>> Recent(Guid customerId, int take, CancellationToken ct);
    Task Save(CancellationToken ct);
}

internal static class CustomerAccess
{
    public static async Task<Result> Till(ITillAccessChecker tills, IShopFeatures features, Guid shopId, Guid deviceId, Guid staffId, string permission, string feature, CancellationToken ct)
    {
        var allowed = await tills.Check(shopId, deviceId, staffId, permission, ct);
        if (!allowed.IsSuccess) return allowed;
        return await features.IsOn(shopId, feature, ct) ? Result.Ok() : Result.Fail("feature.not_enabled", $"'{feature}' is not switched on for this shop.");
    }

    public static async Task<LoyaltyRules> Rules(IShopFeatures features, Guid shopId, CancellationToken ct) =>
        LoyaltyRules.FromJson((await features.For(shopId, ct)).Features.FirstOrDefault(f => f.Flag == "loyalty")?.SettingsJson);
}

// ---------------------------------------------------------------- B7-1: customers

public sealed record CreateCustomer(Guid ShopId, CreateCustomerRequest Request) : ICommand<CustomerResponse>;
public sealed record UpdateCustomer(Guid ShopId, Guid CustomerId, UpdateCustomerRequest Request) : ICommand<CustomerResponse>;
public sealed record FindCustomers(Guid ShopId, Guid DeviceId, Guid StaffId, string Text) : IQuery<List<CustomerResponse>>;
public sealed record GetCustomer(Guid ShopId, Guid CustomerId, Guid DeviceId, Guid StaffId) : IQuery<CustomerResponse>;
public sealed record MergeCustomers(Guid ShopId, Guid TargetCustomerId, MergeCustomersRequest Request) : ICommand<CustomerResponse>;

internal sealed class CustomerHandlers(ICustomerStore store, ITillAccessChecker tills, IOrganisationAccess access, IShopFeatures features, TimeProvider clock)
    : ICommandHandler<CreateCustomer, CustomerResponse>, ICommandHandler<UpdateCustomer, CustomerResponse>, IQueryHandler<FindCustomers, List<CustomerResponse>>,
      IQueryHandler<GetCustomer, CustomerResponse>, ICommandHandler<MergeCustomers, CustomerResponse>
{
    private Task<Result> Allowed(Guid shop, Guid device, Guid staff, CancellationToken ct) => CustomerAccess.Till(tills, features, shop, device, staff, "sale.create", "customers", ct);

    private async Task<CustomerResponse> Respond(Customer c, CancellationToken ct) =>
        new(c.Id, c.Name, c.Phone, c.Email, c.MessageConsent, c.ConsentAt, await store.Balance(c.Id, ct));

    /// <summary>
    /// Customers are matched by phone number: adding someone whose number is already on the list gives back that person, not a second record.
    /// Sending the same request twice is the same customer, because the id is made on the till.
    /// </summary>
    public async Task<Result<CustomerResponse>> Handle(CreateCustomer c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<CustomerResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        if (await store.Get(c.ShopId, r.Id, ct) is { } same) return Result<CustomerResponse>.Ok(await Respond(same, ct));
        var phone = SaPhone.Normalise(r.Phone);
        if (phone is not null && await store.ByPhone(c.ShopId, phone, ct) is { } existing) return Result<CustomerResponse>.Ok(await Respond(existing, ct));

        var created = Customer.Create(r.Id, c.ShopId, r.StaffId, r.DeviceId, new CustomerInput(r.Name, r.Phone, r.Email, r.MessageConsent, r.ConsentSource), clock.GetUtcNow());
        if (!created.IsSuccess) return Result<CustomerResponse>.Fail(created.Error!.Code, created.Error.Message);
        store.Add(created.Value!);
        await store.Save(ct);
        return Result<CustomerResponse>.Ok(await Respond(created.Value!, ct));
    }

    public async Task<Result<CustomerResponse>> Handle(UpdateCustomer c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<CustomerResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var customer = await store.Get(c.ShopId, c.CustomerId, ct);
        if (customer is null) return Result<CustomerResponse>.Fail("customer.not_found", "Customer not found.");

        // A new phone number must not belong to someone else on the list.
        var phone = SaPhone.Normalise(r.Phone);
        if (phone is not null && phone != customer.Phone && await store.ByPhone(c.ShopId, phone, ct) is not null)
            return Result<CustomerResponse>.Fail("customer.phone_taken", "Another customer already has that phone number. Merge them if they are the same person.");

        var updated = customer.Update(new CustomerInput(r.Name, r.Phone, r.Email, r.MessageConsent, r.ConsentSource), clock.GetUtcNow());
        if (!updated.IsSuccess) return Result<CustomerResponse>.Fail(updated.Error!.Code, updated.Error.Message);
        await store.Save(ct);
        return Result<CustomerResponse>.Ok(await Respond(customer, ct));
    }

    public async Task<Result<List<CustomerResponse>>> Handle(FindCustomers q, CancellationToken ct)
    {
        var allowed = await Allowed(q.ShopId, q.DeviceId, q.StaffId, ct);
        if (!allowed.IsSuccess) return Result<List<CustomerResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var text = q.Text?.Trim() ?? "";
        if (text.Length < 3) return Result<List<CustomerResponse>>.Fail("customer.search_short", "Type at least 3 characters of the name or phone number.");

        var found = await store.Search(q.ShopId, text, 20, ct);
        var points = await store.Balances(found.Select(f => f.Id).ToList(), ct);
        return Result<List<CustomerResponse>>.Ok(found.OrderBy(f => f.Name).Select(f => new CustomerResponse(f.Id, f.Name, f.Phone, f.Email, f.MessageConsent, f.ConsentAt, points.GetValueOrDefault(f.Id))).ToList());
    }

    public async Task<Result<CustomerResponse>> Handle(GetCustomer q, CancellationToken ct)
    {
        var allowed = await Allowed(q.ShopId, q.DeviceId, q.StaffId, ct);
        if (!allowed.IsSuccess) return Result<CustomerResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var customer = await store.Get(q.ShopId, q.CustomerId, ct);
        return customer is null ? Result<CustomerResponse>.Fail("customer.not_found", "Customer not found.") : Result<CustomerResponse>.Ok(await Respond(customer, ct));
    }

    /// <summary>
    /// The owner joins two records of one person. The source's points move to the target as ledger entries (nothing is edited or deleted), and the source
    /// stays on file, marked as merged, so old sales that point at it still lead somewhere.
    /// </summary>
    public async Task<Result<CustomerResponse>> Handle(MergeCustomers c, CancellationToken ct)
    {
        if (!await access.Has(c.Request.CallerPersonId, c.ShopId, "shop.settings", ct)) return Result<CustomerResponse>.Fail("permission.denied", "Only the owner can merge customers.");
        if (!await features.IsOn(c.ShopId, "customers", ct)) return Result<CustomerResponse>.Fail("feature.not_enabled", "'customers' is not switched on for this shop.");
        if (c.TargetCustomerId == c.Request.SourceCustomerId) return Result<CustomerResponse>.Fail("customer.merge_self", "Choose two different customers.");

        var target = await store.Get(c.ShopId, c.TargetCustomerId, ct);
        var source = await store.Get(c.ShopId, c.Request.SourceCustomerId, ct);
        if (target is null || source is null) return Result<CustomerResponse>.Fail("customer.not_found", "Customer not found.");
        if (target.IsMerged || source.IsMerged) return Result<CustomerResponse>.Fail("customer.merged", "One of these customers was already merged into another.");

        var now = clock.GetUtcNow();
        var points = await store.Balance(source.Id, ct);
        if (points != 0)
        {
            store.Add(new LoyaltyEntry(source.Id, c.ShopId, LoyaltyEntryType.Merge, -points, $"merge:{source.Id:N}:out", null, $"Merged into {target.Name}", null, now));
            store.Add(new LoyaltyEntry(target.Id, c.ShopId, LoyaltyEntryType.Merge, points, $"merge:{source.Id:N}:in", null, $"Merged from {source.Name}", null, now));
        }
        // Consent is never widened by a merge: it stays what the surviving record says.
        source.MergeInto(target.Id);
        await store.Save(ct);
        return Result<CustomerResponse>.Ok(await Respond(target, ct));
    }
}

// ---------------------------------------------------------------- B7-2: loyalty points

public sealed record GetPoints(Guid ShopId, Guid CustomerId, Guid DeviceId, Guid StaffId) : IQuery<PointsResponse>;
public sealed record RedeemPoints(Guid ShopId, Guid CustomerId, RedeemPointsRequest Request) : ICommand<RedeemPointsResponse>;
public sealed record ReleasePoints(Guid ShopId, Guid CustomerId, ReleasePointsRequest Request) : ICommand<PointsResponse>;
public sealed record AdjustPoints(Guid ShopId, Guid CustomerId, AdjustPointsRequest Request) : ICommand<PointsResponse>;

internal sealed class LoyaltyHandlers(ICustomerStore store, ITillAccessChecker tills, IShopFeatures features, TimeProvider clock)
    : IQueryHandler<GetPoints, PointsResponse>, ICommandHandler<RedeemPoints, RedeemPointsResponse>, ICommandHandler<AdjustPoints, PointsResponse>, ICommandHandler<ReleasePoints, PointsResponse>
{
    private Task<Result> Allowed(Guid shop, Guid device, Guid staff, CancellationToken ct) => CustomerAccess.Till(tills, features, shop, device, staff, "sale.create", "loyalty", ct);

    public async Task<Result<PointsResponse>> Handle(GetPoints q, CancellationToken ct)
    {
        var allowed = await Allowed(q.ShopId, q.DeviceId, q.StaffId, ct);
        if (!allowed.IsSuccess) return Result<PointsResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var customer = await store.Get(q.ShopId, q.CustomerId, ct);
        if (customer is null) return Result<PointsResponse>.Fail("customer.not_found", "Customer not found.");
        return Result<PointsResponse>.Ok(await Respond(customer, ct));
    }

    /// <summary>The cashier abandoned the sale after redeeming points: the points go back, once. Asking again changes nothing.</summary>
    public async Task<Result<PointsResponse>> Handle(ReleasePoints c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<PointsResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var customer = await store.Get(c.ShopId, c.CustomerId, ct);
        if (customer is null || customer.IsMerged) return Result<PointsResponse>.Fail("customer.not_found", "Customer not found.");
        var reference = r.Reference?.Trim() ?? "";
        var redeemed = await store.EntryByReference(customer.Id, $"redeem:{reference}", ct);
        if (redeemed is null || redeemed.Type != LoyaltyEntryType.Redeem) return Result<PointsResponse>.Fail("loyalty.redemption_not_found", "No redemption with that reference.");
        var releaseReference = $"release:{reference}";
        if (await store.EntryByReference(customer.Id, releaseReference, ct) is null)
        {
            store.Add(new LoyaltyEntry(customer.Id, c.ShopId, LoyaltyEntryType.Reverse, -redeemed.Points, releaseReference, null, "Sale not completed", r.StaffId, clock.GetUtcNow()));
            await store.Save(ct);
        }
        return Result<PointsResponse>.Ok(await Respond(customer, ct));
    }

    /// <summary>Takes points off and gives back what they are worth, which the till pays as a "voucher" part of the sale. Repeating the reference changes nothing.</summary>
    public async Task<Result<RedeemPointsResponse>> Handle(RedeemPoints c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<RedeemPointsResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var customer = await store.Get(c.ShopId, c.CustomerId, ct);
        if (customer is null || customer.IsMerged) return Result<RedeemPointsResponse>.Fail("customer.not_found", "Customer not found.");
        if (string.IsNullOrWhiteSpace(r.Reference) || r.Reference.Length > 80) return Result<RedeemPointsResponse>.Fail("loyalty.reference_required", "A redemption needs a reference so it cannot happen twice.");

        var rules = await CustomerAccess.Rules(features, c.ShopId, ct);
        var reference = $"redeem:{r.Reference.Trim()}";
        if (await store.EntryByReference(customer.Id, reference, ct) is { } done)
            return Result<RedeemPointsResponse>.Ok(new RedeemPointsResponse($"points:{done.Id:N}", -done.Points, rules.ValueCents(-done.Points), await store.Balance(customer.Id, ct)));

        if (r.Points < rules.MinRedeemPoints) return Result<RedeemPointsResponse>.Fail("loyalty.below_minimum", $"Redeem at least {rules.MinRedeemPoints} points at a time.");
        var balance = await store.Balance(customer.Id, ct);
        if (r.Points > balance) return Result<RedeemPointsResponse>.Fail("loyalty.insufficient", $"The customer has {balance} points.");

        var entry = new LoyaltyEntry(customer.Id, c.ShopId, LoyaltyEntryType.Redeem, -r.Points, reference, null, null, r.StaffId, clock.GetUtcNow());
        store.Add(entry);
        await store.Save(ct);
        return Result<RedeemPointsResponse>.Ok(new RedeemPointsResponse($"points:{entry.Id:N}", r.Points, rules.ValueCents(r.Points), balance - r.Points));
    }

    /// <summary>A manager corrects a balance (a missed sale, a mistake). Always with a reason, always as an entry, never by editing.</summary>
    public async Task<Result<PointsResponse>> Handle(AdjustPoints c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<PointsResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var approver = await tills.Check(c.ShopId, r.DeviceId, r.ApprovedBy, "sale.refund_approve", ct);
        if (!approver.IsSuccess) return Result<PointsResponse>.Fail("loyalty.approver_not_allowed", "Only a manager can adjust points.");
        var customer = await store.Get(c.ShopId, c.CustomerId, ct);
        if (customer is null || customer.IsMerged) return Result<PointsResponse>.Fail("customer.not_found", "Customer not found.");
        if (r.Points == 0 || Math.Abs(r.Points) > 100_000) return Result<PointsResponse>.Fail("loyalty.adjustment_invalid", "Adjust by a number of points that is not zero and not more than 100,000.");
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Trim().Length > 200) return Result<PointsResponse>.Fail("loyalty.reason_required", "Say why (up to 200 characters).");
        if (r.Points < 0 && -r.Points > await store.Balance(customer.Id, ct)) return Result<PointsResponse>.Fail("loyalty.insufficient", "That is more points than the customer has.");

        store.Add(new LoyaltyEntry(customer.Id, c.ShopId, LoyaltyEntryType.Adjust, r.Points, $"adjust:{Guid.CreateVersion7():N}", null, r.Reason.Trim(), r.ApprovedBy, clock.GetUtcNow()));
        await store.Save(ct);
        return Result<PointsResponse>.Ok(await Respond(customer, ct));
    }

    private async Task<PointsResponse> Respond(Customer customer, CancellationToken ct)
    {
        var rules = await CustomerAccess.Rules(features, customer.ShopId, ct);
        var balance = await store.Balance(customer.Id, ct);
        return new PointsResponse(customer.Id, balance, rules.ValueCents(balance), rules.MinRedeemPoints,
            (await store.Recent(customer.Id, 20, ct)).Select(e => new PointsEntryResponse(e.Type.ToString().ToLowerInvariant(), e.Points, e.Note, e.CreatedAt, e.SaleId)).ToList());
    }
}

// ---------------------------------------------------------------- earning and taking back, from what happens at the till

/// <summary>A sale for a customer earns points, once: the sale id is the reference, so hearing about the same sale twice earns nothing more.</summary>
internal sealed class EarnPointsOnSale(ICustomerStore store, IShopFeatures features) : IDomainEventHandler<SaleRecorded>
{
    public async Task Handle(SaleRecorded e, CancellationToken ct)
    {
        if (!await features.IsOn(e.ShopId, "loyalty", ct)) return;
        var customer = await store.Get(e.ShopId, e.CustomerId, ct);
        if (customer is null) return;

        // A customer merged into another earns for the one that carries on.
        var holder = customer;
        for (var hops = 0; holder.MergedIntoId is { } next && hops < 5; hops++) holder = await store.Get(e.ShopId, next, ct) ?? holder;

        var reference = $"sale:{e.SaleId:N}";
        if (await store.EntryByReference(holder.Id, reference, ct) is not null) return;
        var points = (await CustomerAccess.Rules(features, e.ShopId, ct)).Earned(e.TotalCents);
        if (points <= 0) return;

        store.Add(new LoyaltyEntry(holder.Id, e.ShopId, LoyaltyEntryType.Earn, points, reference, e.SaleId, null, null, e.SoldAt));
        await store.Save(ct);
    }
}

/// <summary>A cancelled sale gives its points back, whatever has happened since: a customer may end up owing points, which later sales pay off.</summary>
internal sealed class ReversePointsOnCancel(ICustomerStore store, TimeProvider clock) : IDomainEventHandler<SaleCancelled>
{
    public async Task Handle(SaleCancelled e, CancellationToken ct)
    {
        foreach (var earned in (await store.EntriesForSale(e.SaleId, ct)).Where(x => x.Type == LoyaltyEntryType.Earn))
        {
            var reference = $"cancel:{e.SaleId:N}";
            if (await store.EntryByReference(earned.CustomerId, reference, ct) is not null) continue;
            store.Add(new LoyaltyEntry(earned.CustomerId, earned.ShopId, LoyaltyEntryType.Reverse, -earned.Points, reference, e.SaleId, "Sale cancelled", null, clock.GetUtcNow()));
        }
        await store.Save(ct);
    }
}

/// <summary>A refund takes back the same share of the points as of the money.</summary>
internal sealed class ReversePointsOnRefund(ICustomerStore store, TimeProvider clock) : IDomainEventHandler<SaleRefunded>
{
    public async Task Handle(SaleRefunded e, CancellationToken ct)
    {
        foreach (var earned in (await store.EntriesForSale(e.SaleId, ct)).Where(x => x.Type == LoyaltyEntryType.Earn))
        {
            var reference = $"refund:{e.RefundId:N}";
            if (await store.EntryByReference(earned.CustomerId, reference, ct) is not null) continue;
            var back = LoyaltyRules.ShareOf(earned.Points, e.RefundCents, e.SaleTotalCents);
            if (back <= 0) continue;
            store.Add(new LoyaltyEntry(earned.CustomerId, earned.ShopId, LoyaltyEntryType.Reverse, -back, reference, e.SaleId, "Goods refunded", null, clock.GetUtcNow()));
        }
        await store.Save(ct);
    }
}

/// <summary>Lets other modules (Bookings) ask whether a customer agreed to be messaged, and where to reach them.</summary>
public sealed class CustomerDirectory(ICustomerStore store) : ICustomerDirectory
{
    public async Task<CustomerInfo?> Get(Guid shopId, Guid customerId, CancellationToken ct)
    {
        var customer = await store.Get(shopId, customerId, ct);
        for (var hops = 0; customer?.MergedIntoId is { } next && hops < 5; hops++) customer = await store.Get(shopId, next, ct);
        return customer is null ? null : new CustomerInfo(customer.Id, customer.Name, customer.Phone, customer.Email, customer.MessageConsent);
    }
}
