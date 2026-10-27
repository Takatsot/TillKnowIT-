using ShopCore.Application.Sync;
using ShopCore.Contracts;
using ShopCore.Domain;
using ShopCore.Domain.Selling;
using Identity.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Selling;

// ---------------------------------------------------------------- B6-2: lay-by

internal static class LaybyMapping
{
    public static LaybyResponse ToResponse(LaybyPlan p) => new(
        p.Id, p.Reference, p.CustomerName, p.CustomerPhone, p.Lines.Select(TicketMapping.ToDto).ToList(), p.TotalCents, p.PaidCents, p.BalanceCents, p.DueOn,
        p.Status.ToString().ToLowerInvariant(), p.CancellationFeeCents, p.RefundDueCents, p.Payments.OrderBy(x => x.PaidAt).Select(x => new LaybyPaymentDto(x.AmountCents, x.MethodCode, x.PaidAt)).ToList());
}

public sealed record CreateLayby(Guid ShopId, CreateLaybyRequest Request) : ICommand<LaybyResponse>;
public sealed record PayLayby(Guid ShopId, Guid PlanId, PayLaybyRequest Request) : ICommand<LaybyResponse>;
public sealed record CancelLayby(Guid ShopId, Guid PlanId, TillCall Call) : ICommand<LaybyResponse>;
public sealed record CollectLayby(Guid ShopId, Guid PlanId, CollectLaybyRequest Request) : ICommand<LaybyResponse>;
public sealed record ListLaybys(Guid ShopId, Guid DeviceId, Guid StaffId) : IQuery<List<LaybyResponse>>;
public sealed record GetLaybyReservations(Guid ShopId, Guid DeviceId, Guid StaffId) : IQuery<Dictionary<Guid, int>>;
public sealed record ExpireDueLaybys : ICommand<int>;

internal sealed class LaybyHandlers(ISellingStore store, ITillAccessChecker tills, IFeatureGate gate, TimeProvider clock)
    : ICommandHandler<CreateLayby, LaybyResponse>, ICommandHandler<PayLayby, LaybyResponse>, ICommandHandler<CancelLayby, LaybyResponse>,
      ICommandHandler<CollectLayby, LaybyResponse>, IQueryHandler<ListLaybys, List<LaybyResponse>>, IQueryHandler<GetLaybyReservations, Dictionary<Guid, int>>,
      ICommandHandler<ExpireDueLaybys, int>
{
    private Task<Result> Allowed(Guid shop, Guid device, Guid staff, CancellationToken ct) => SellingAccess.TillAndFeature(tills, gate, shop, device, staff, "sale.create", "layby", ct);

    private async Task<LaybyTerms> Terms(Guid shop, CancellationToken ct) => LaybyTerms.FromJson(await gate.Settings(shop, "layby", ct));

    public async Task<Result<LaybyResponse>> Handle(CreateLayby c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<LaybyResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var same = await store.GetLayby(c.ShopId, r.Id, ct);
        if (same is not null) return Result<LaybyResponse>.Ok(LaybyMapping.ToResponse(same));   // sent twice: the same plan

        var plan = LaybyPlan.Create(r.Id, c.ShopId, r.StaffId, r.DeviceId, r.Reference, r.CustomerName, r.CustomerPhone, r.CustomerId, TicketMapping.ToLines(r.Lines),
            r.DepositCents, r.DepositMethod?.Trim().ToLowerInvariant() ?? "", r.DueOn, await Terms(c.ShopId, ct), clock.GetUtcNow());
        if (!plan.IsSuccess) return Result<LaybyResponse>.Fail(plan.Error!.Code, plan.Error.Message);
        store.Add(plan.Value!);
        await store.Save(ct);
        return Result<LaybyResponse>.Ok(LaybyMapping.ToResponse(plan.Value!));
    }

    public async Task<Result<LaybyResponse>> Handle(PayLayby c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<LaybyResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var plan = await store.GetLayby(c.ShopId, c.PlanId, ct);
        if (plan is null) return Result<LaybyResponse>.Fail("layby.not_found", "Lay-by not found.");

        var paid = plan.Pay(r.AmountCents, r.MethodCode?.Trim().ToLowerInvariant() ?? "", r.StaffId, r.DeviceId, clock.GetUtcNow());
        if (!paid.IsSuccess) return Result<LaybyResponse>.Fail(paid.Error!.Code, paid.Error.Message);
        await store.Save(ct);
        return Result<LaybyResponse>.Ok(LaybyMapping.ToResponse(plan));
    }

    public async Task<Result<LaybyResponse>> Handle(CancelLayby c, CancellationToken ct)
    {
        var allowed = await Allowed(c.ShopId, c.Call.DeviceId, c.Call.StaffId, ct);
        if (!allowed.IsSuccess) return Result<LaybyResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var plan = await store.GetLayby(c.ShopId, c.PlanId, ct);
        if (plan is null) return Result<LaybyResponse>.Fail("layby.not_found", "Lay-by not found.");

        var cancelled = plan.Cancel(await Terms(c.ShopId, ct));
        if (!cancelled.IsSuccess) return Result<LaybyResponse>.Fail(cancelled.Error!.Code, cancelled.Error.Message);
        await store.Save(ct);
        return Result<LaybyResponse>.Ok(LaybyMapping.ToResponse(plan));
    }

    /// <summary>The till has rung the paid-off goods up as an ordinary sale (paid by "layby") and says which sale it was.</summary>
    public async Task<Result<LaybyResponse>> Handle(CollectLayby c, CancellationToken ct)
    {
        var allowed = await Allowed(c.ShopId, c.Request.DeviceId, c.Request.StaffId, ct);
        if (!allowed.IsSuccess) return Result<LaybyResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var plan = await store.GetLayby(c.ShopId, c.PlanId, ct);
        if (plan is null) return Result<LaybyResponse>.Fail("layby.not_found", "Lay-by not found.");

        var summary = await store.SaleSummary(c.ShopId, c.Request.SaleId, ct);
        if (summary is null) return Result<LaybyResponse>.Fail("layby.sale_unknown", "Send the sale to the server first.");
        if (summary.PaymentMethodCode != PaymentMethods.Layby || summary.TotalCents != plan.TotalCents) return Result<LaybyResponse>.Fail("layby.sale_mismatch", "That sale is not this lay-by.");

        var collected = plan.Collect(c.Request.SaleId);
        if (!collected.IsSuccess) return Result<LaybyResponse>.Fail(collected.Error!.Code, collected.Error.Message);
        await store.Save(ct);
        return Result<LaybyResponse>.Ok(LaybyMapping.ToResponse(plan));
    }

    public async Task<Result<List<LaybyResponse>>> Handle(ListLaybys q, CancellationToken ct)
    {
        var allowed = await Allowed(q.ShopId, q.DeviceId, q.StaffId, ct);
        if (!allowed.IsSuccess) return Result<List<LaybyResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return Result<List<LaybyResponse>>.Ok((await store.Laybys(q.ShopId, ct)).OrderByDescending(p => p.CreatedAt).Select(LaybyMapping.ToResponse).ToList());
    }

    /// <summary>Quantity set aside per product by lay-bys that are active or paid off and waiting. Stock on hand is unchanged until they are collected.</summary>
    public async Task<Result<Dictionary<Guid, int>>> Handle(GetLaybyReservations q, CancellationToken ct)
    {
        var allowed = await Allowed(q.ShopId, q.DeviceId, q.StaffId, ct);
        if (!allowed.IsSuccess) return Result<Dictionary<Guid, int>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return Result<Dictionary<Guid, int>>.Ok((await store.Laybys(q.ShopId, ct)).Where(p => p.ReservesStock).SelectMany(p => p.Lines)
            .GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity)));
    }

    /// <summary>The worker's daily job: a lay-by past its due date that is not paid off lapses on the shop's cancellation terms.</summary>
    public async Task<Result<int>> Handle(ExpireDueLaybys c, CancellationToken ct)
    {
        var today = BusinessDay.Of(clock.GetUtcNow());
        var expired = 0;
        var termsByShop = new Dictionary<Guid, LaybyTerms>();
        foreach (var plan in await store.ActiveLaybysPastDue(today, 200, ct))
        {
            if (!termsByShop.TryGetValue(plan.ShopId, out var terms)) termsByShop[plan.ShopId] = terms = await Terms(plan.ShopId, ct);
            if (plan.ExpireIfDue(today, terms)) expired++;
        }
        if (expired > 0) await store.Save(ct);
        return Result<int>.Ok(expired);
    }
}

// ---------------------------------------------------------------- B7-3: gift vouchers

public sealed record IssueVoucher(Guid ShopId, IssueVoucherRequest Request) : ICommand<VoucherIssued>;
public sealed record CheckVoucher(Guid ShopId, string Code, Guid DeviceId, Guid StaffId) : IQuery<VoucherResponse>;
public sealed record RedeemVoucher(Guid ShopId, RedeemVoucherRequest Request) : ICommand<VoucherResponse>;

internal sealed class VoucherHandlers(ISellingStore store, ITillAccessChecker tills, IFeatureGate gate, TimeProvider clock)
    : ICommandHandler<IssueVoucher, VoucherIssued>, IQueryHandler<CheckVoucher, VoucherResponse>, ICommandHandler<RedeemVoucher, VoucherResponse>
{
    private Task<Result> Allowed(Guid shop, Guid device, Guid staff, CancellationToken ct) => SellingAccess.TillAndFeature(tills, gate, shop, device, staff, "sale.create", "vouchers", ct);

    /// <summary>Validity in months comes from the voucher feature's settings (validMonths), 12 if not set.</summary>
    private async Task<int> ValidMonths(Guid shop, CancellationToken ct)
    {
        var json = await gate.Settings(shop, "vouchers", ct);
        if (string.IsNullOrWhiteSpace(json)) return GiftVoucher.DefaultValidMonths;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("validMonths", out var v) && v.TryGetInt32(out var m) && m is >= 1 and <= 60 ? m : GiftVoucher.DefaultValidMonths;
        }
        catch (System.Text.Json.JsonException) { return GiftVoucher.DefaultValidMonths; }
    }

    public async Task<Result<VoucherIssued>> Handle(IssueVoucher c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<VoucherIssued>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (string.IsNullOrWhiteSpace(r.Reference) || r.Reference.Length > 80) return Result<VoucherIssued>.Fail("voucher.reference_required", "A voucher sale needs a reference so it cannot be issued twice.");

        var issued = GiftVoucher.Issue(c.ShopId, r.AmountCents, await ValidMonths(c.ShopId, ct), r.StaffId, r.DeviceId, r.SaleId, r.Reference.Trim(), clock.GetUtcNow(), r.PaidWith?.Trim().ToLowerInvariant());
        if (!issued.IsSuccess) return Result<VoucherIssued>.Fail(issued.Error!.Code, issued.Error.Message);
        store.Add(issued.Value.Voucher);
        await store.Save(ct);
        return Result<VoucherIssued>.Ok(new VoucherIssued(issued.Value.Voucher.Id, issued.Value.Code, issued.Value.Voucher.BalanceCents, issued.Value.Voucher.ExpiresOn));
    }

    public async Task<Result<VoucherResponse>> Handle(CheckVoucher q, CancellationToken ct)
    {
        var allowed = await Allowed(q.ShopId, q.DeviceId, q.StaffId, ct);
        if (!allowed.IsSuccess) return Result<VoucherResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var voucher = await store.VoucherByHash(q.ShopId, GiftVoucher.Hash(q.Code ?? ""), ct);
        return voucher is null ? Result<VoucherResponse>.Fail("voucher.not_found", "That code is not a voucher of this shop.") : Result<VoucherResponse>.Ok(ToResponse(voucher));
    }

    public async Task<Result<VoucherResponse>> Handle(RedeemVoucher c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<VoucherResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (string.IsNullOrWhiteSpace(r.Reference) || r.Reference.Length > 80) return Result<VoucherResponse>.Fail("voucher.reference_required", "A redemption needs a reference so it cannot happen twice.");
        var voucher = await store.VoucherByHash(c.ShopId, GiftVoucher.Hash(r.Code ?? ""), ct);
        if (voucher is null) return Result<VoucherResponse>.Fail("voucher.not_found", "That code is not a voucher of this shop.");

        var redeemed = voucher.Redeem(r.AmountCents, r.SaleId, r.Reference.Trim(), r.StaffId, clock.GetUtcNow());
        if (!redeemed.IsSuccess) return Result<VoucherResponse>.Fail(redeemed.Error!.Code, redeemed.Error.Message);
        await store.Save(ct);
        return Result<VoucherResponse>.Ok(ToResponse(voucher));
    }

    private VoucherResponse ToResponse(GiftVoucher v) =>
        new(v.Id, v.CodeLast4, v.BalanceCents, v.ExpiresOn, v.IsVoid, v.ExpiresOn is { } e && BusinessDay.Of(clock.GetUtcNow()) > e);
}

// ---------------------------------------------------------------- B6-3: serial numbers

public sealed record RecordSerials(Guid ShopId, Guid SaleId, RecordSerialsRequest Request) : ICommand<Unit>;
public sealed record FindSerials(Guid ShopId, string Text, Guid DeviceId, Guid StaffId) : IQuery<List<SerialResponse>>;

internal sealed class SerialHandlers(ISellingStore store, ITillAccessChecker tills, IFeatureGate gate, TimeProvider clock)
    : ICommandHandler<RecordSerials, Unit>, IQueryHandler<FindSerials, List<SerialResponse>>
{
    private Task<Result> Allowed(Guid shop, Guid device, Guid staff, CancellationToken ct) => SellingAccess.TillAndFeature(tills, gate, shop, device, staff, "sale.create", "serial_numbers", ct);

    public async Task<Result<Unit>> Handle(RecordSerials c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await Allowed(c.ShopId, r.DeviceId, r.StaffId, ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var sold = await store.SoldQuantities(c.ShopId, c.SaleId, ct);
        if (sold.Count == 0) return Result<Unit>.Fail("serial.sale_unknown", "Send the sale to the server first.");
        var items = r.Items ?? [];
        if (items.Count is 0 or > 200) return Result<Unit>.Fail("serial.count_invalid", "Record from 1 to 200 serial numbers at a time.");

        var products = (await store.ProductsByIds(c.ShopId, items.Select(i => i.ProductId).Distinct().ToList(), ct)).ToDictionary(p => p.Id);
        var already = await store.SerialCounts(c.ShopId, c.SaleId, ct);
        var inRequest = new HashSet<(Guid, string)>();
        var perProduct = new Dictionary<Guid, int>();
        var made = new List<SerialNumberRecord>();
        var now = clock.GetUtcNow();

        foreach (var item in items)
        {
            if (!products.TryGetValue(item.ProductId, out var product) || !sold.TryGetValue(item.ProductId, out var quantity)) return Result<Unit>.Fail("serial.not_in_sale", "That item was not part of the sale.");
            if (!product.RequiresSerial) return Result<Unit>.Fail("serial.not_required", $"{product.Name} does not take serial numbers.");
            var clean = SerialNumberRecord.Clean(item.Serial);
            if (!clean.IsSuccess) return Result<Unit>.Fail(clean.Error!.Code, clean.Error.Message);

            if (!inRequest.Add((item.ProductId, clean.Value!)) || await store.SerialExists(c.ShopId, item.ProductId, clean.Value!, ct))
                return Result<Unit>.Fail("serial.already_sold", $"Serial {clean.Value} has already been sold.");
            perProduct[item.ProductId] = perProduct.GetValueOrDefault(item.ProductId) + 1;
            if (already.GetValueOrDefault(item.ProductId) + perProduct[item.ProductId] > quantity) return Result<Unit>.Fail("serial.too_many", $"{product.Name}: only {quantity} sold.");
            made.Add(new SerialNumberRecord(c.ShopId, item.ProductId, clean.Value!, c.SaleId, now, r.StaffId));
        }

        foreach (var m in made) store.Add(m);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    public async Task<Result<List<SerialResponse>>> Handle(FindSerials q, CancellationToken ct)
    {
        var allowed = await Allowed(q.ShopId, q.DeviceId, q.StaffId, ct);
        if (!allowed.IsSuccess) return Result<List<SerialResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        var text = q.Text?.Trim() ?? "";
        if (text.Length < 3) return Result<List<SerialResponse>>.Fail("serial.search_short", "Type at least 3 characters of the serial number.");

        var found = await store.FindSerials(q.ShopId, text, 50, ct);
        var products = (await store.ProductsByIds(q.ShopId, found.Select(f => f.ProductId).Distinct().ToList(), ct)).ToDictionary(p => p.Id);
        return Result<List<SerialResponse>>.Ok(found.Select(f => new SerialResponse(f.ProductId, products.TryGetValue(f.ProductId, out var p) ? p.Name : "", f.Serial, f.SaleId, f.SoldAt)).ToList());
    }
}

// ---------------------------------------------------------------- B2-4: refunds online

public sealed record CreateOnlineRefund(Guid ShopId, Guid SaleId, OnlineRefundRequest Request) : ICommand<OnlineRefundResponse>;

/// <summary>
/// The online path to a refund. Same rules as one synced from a till, plus the two things that need a connection: a refund to a voucher (the code is shown
/// once, here) and a refund of a sale paid by wallet or lay-by.
/// </summary>
internal sealed class OnlineRefundHandler(IRefundEffects effects, IRefundStore refunds, ISellingStore selling, ITillAccessChecker tills, IFeatureGate gate, TimeProvider clock)
    : ICommandHandler<CreateOnlineRefund, OnlineRefundResponse>
{
    public async Task<Result<OnlineRefundResponse>> Handle(CreateOnlineRefund c, CancellationToken ct)
    {
        var r = c.Request;
        var allowed = await SellingAccess.TillAndFeature(tills, gate, c.ShopId, r.DeviceId, r.StaffId, "sale.refund", "refunds", ct);
        if (!allowed.IsSuccess) return Result<OnlineRefundResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if ((await refunds.ExistingIds([r.Id], ct)).Contains(r.Id)) return Result<OnlineRefundResponse>.Fail("refund.already_processed", "This refund has already been made.");

        var sale = await selling.SaleSummary(c.ShopId, c.SaleId, ct);
        if (sale is null) return Result<OnlineRefundResponse>.Fail("refund.sale_unknown", "The sale is not on the server.");
        if (await selling.SaleCancelled(c.SaleId, ct)) return Result<OnlineRefundResponse>.Fail("refund.sale_cancelled", "A cancelled sale cannot be refunded.");

        var sold = (await refunds.SoldLines(c.ShopId, [c.SaleId], ct)).GetValueOrDefault(c.SaleId) ?? [];
        var already = (await refunds.RefundedSoFar([c.SaleId], ct)).Select(x => new RefundedSoFar(x.ProductId, x.Quantity)).ToList();
        var json = await gate.Settings(c.ShopId, "refunds", ct);
        var threshold = RefundRules.DefaultApprovalThresholdCents;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("approvalThresholdCents", out var v) && v.TryGetInt64(out var cents) && cents >= 0) threshold = cents;
            }
            catch (System.Text.Json.JsonException) { }
        }

        var now = clock.GetUtcNow();
        var upload = new RefundUpload(r.Id, r.StaffId, c.SaleId, r.Reason, r.MethodCode, r.ApprovedBy, r.Lines, now);
        var processor = new RefundProcessor(effects, refunds);
        var outcome = await processor.Process(upload, new RefundProcessor.Context(c.ShopId, r.DeviceId, r.StaffId, sale, sold, already, threshold, OnlineAllowed: true),
            (staff, permission) => tills.Check(c.ShopId, r.DeviceId, staff, permission, ct), now, ct);
        if (!outcome.IsSuccess) return Result<OnlineRefundResponse>.Fail(outcome.Error!.Code, outcome.Error.Message);

        VoucherIssued? voucher = null;
        if (outcome.Value!.MethodCode == ExtraPaymentMethods.Voucher)
        {
            var issued = GiftVoucher.Issue(c.ShopId, outcome.Value.Refund.AmountCents, GiftVoucher.DefaultValidMonths, r.StaffId, r.DeviceId, c.SaleId, $"refund:{r.Id:N}", now);
            if (!issued.IsSuccess) return Result<OnlineRefundResponse>.Fail(issued.Error!.Code, issued.Error.Message);
            selling.Add(issued.Value.Voucher);
            voucher = new VoucherIssued(issued.Value.Voucher.Id, issued.Value.Code, issued.Value.Voucher.BalanceCents, issued.Value.Voucher.ExpiresOn);
        }

        await selling.Save(ct);   // one unit of work: the refund, the stock and the voucher are saved together
        return Result<OnlineRefundResponse>.Ok(new OnlineRefundResponse(r.Id, outcome.Value.Refund.AmountCents, outcome.Value.MethodCode, voucher));
    }
}
