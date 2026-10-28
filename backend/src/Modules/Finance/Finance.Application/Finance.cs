using Billing.Contracts;
using Finance.Contracts;
using Finance.Domain;
using Identity.Contracts;
using Marketplace.Contracts;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Finance.Application;

public sealed record PartnerApplication(Guid ApplicationId, Guid ShopId, long RequestedCents, List<MonthlySales> SalesHistory);
public sealed record PartnerDecision(bool Approved, string Reference, long ApprovedCents, int TermDays, string? Reason);
public sealed record PartnerDisbursement(string Reference, long RepayCents, DateOnly DueOn);
public sealed record PartnerRepayment(string Reference, string DrawReference, long AmountCents, DateTimeOffset PaidAt);

/// <summary>A registered credit provider. It decides and lends; we pass on only what the owner agreed to share and track what happens.</summary>
public interface IFinancePartner
{
    string Code { get; }
    Task<Result<PartnerDecision>> Apply(PartnerApplication application, CancellationToken ct);

    /// <summary>The partner pays the supplier. We never touch the money.</summary>
    Task<Result<PartnerDisbursement>> Disburse(string applicationReference, Guid orderId, string supplierName, long principalCents, CancellationToken ct);

    bool VerifyCallback(string? signature, string body, DateTimeOffset now);
    Result<PartnerRepayment> ParseRepayment(string body);
}

public interface IFinanceStore
{
    void Add(CreditConsent consent);
    Task<CreditConsent?> Consent(Guid shopId, Guid id, CancellationToken ct);
    Task<CreditConsent?> ActiveConsent(Guid shopId, string partnerCode, CancellationToken ct);
    Task<List<CreditConsent>> Consents(Guid shopId, CancellationToken ct);
    void Add(DataShareRecord record);
    Task<List<DataShareRecord>> Shares(Guid shopId, int take, CancellationToken ct);

    void Add(CreditApplication application);
    Task<CreditApplication?> Application(Guid id, CancellationToken ct);
    Task<List<CreditApplication>> Applications(Guid shopId, CancellationToken ct);

    void Add(CreditDraw draw);
    Task<CreditDraw?> DrawForOrder(Guid orderId, CancellationToken ct);
    Task<CreditDraw?> DrawByReference(string partnerCode, string reference, CancellationToken ct);
    Task<List<CreditDraw>> Draws(Guid shopId, CancellationToken ct);
    Task<List<CreditDraw>> DrawsOf(Guid applicationId, CancellationToken ct);

    void Add(CreditRepayment repayment);
    Task<bool> RepaymentExists(string reference, CancellationToken ct);
    Task Save(CancellationToken ct);
}

internal static class FinanceMapping
{
    public static ConsentResponse ToResponse(CreditConsent c) => new(c.Id, c.PartnerCode, c.Months, c.GrantedAt, c.WithdrawnAt, c.IsActive);

    public static ApplicationResponse ToResponse(CreditApplication a, long used) =>
        new(a.Id, a.PartnerCode, a.Status.ToString().ToLowerInvariant(), a.RequestedCents, a.ApprovedCents, Math.Max(0, a.ApprovedCents - used), a.TermDays, a.DecisionReason, a.DecidedAt);

    public static DrawResponse ToResponse(CreditDraw d) => new(d.Id, d.OrderId, d.SupplierName, d.PrincipalCents, d.RepayCents, d.RepaidCents, d.DueOn, d.Status.ToString().ToLowerInvariant());

    /// <summary>Credit in use: what the shop has not yet paid back on this application. It comes back as repayments arrive.</summary>
    public static long Used(IEnumerable<CreditDraw> draws) => draws.Sum(d => d.OutstandingPrincipalCents);
}

public sealed record GrantConsent(Guid ShopId, GrantConsentRequest Request) : ICommand<ConsentResponse>;
public sealed record WithdrawConsent(Guid ShopId, Guid ConsentId, Guid PersonId) : ICommand<ConsentResponse>;
public sealed record ApplyForCredit(Guid ShopId, ApplyRequest Request) : ICommand<ApplicationResponse>;
public sealed record DrawOnCredit(Guid ShopId, DrawRequest Request) : ICommand<DrawResponse>;
public sealed record GetCreditSummary(Guid ShopId, Guid PersonId) : IQuery<CreditSummaryResponse>;
public sealed record HandleFinanceCallback(string PartnerCode, string? Signature, string Body) : ICommand<bool>;

internal sealed class FinanceHandlers(IFinanceStore store, IEnumerable<IFinancePartner> partners, IOrganisationAccess access, IEntitlements entitlements, IBranchDirectory branches,
    ISalesHistory history, IOrderPayments orders, TimeProvider clock)
    : ICommandHandler<GrantConsent, ConsentResponse>, ICommandHandler<WithdrawConsent, ConsentResponse>, ICommandHandler<ApplyForCredit, ApplicationResponse>,
      ICommandHandler<DrawOnCredit, DrawResponse>, IQueryHandler<GetCreditSummary, CreditSummaryResponse>, ICommandHandler<HandleFinanceCallback, bool>
{
    private IFinancePartner? Partner(string? code) => partners.FirstOrDefault(p => p.Code.Equals(code?.Trim(), StringComparison.OrdinalIgnoreCase));

    private async Task<bool> MayManage(Guid personId, Guid shopId, CancellationToken ct) => await access.Has(personId, shopId, "shop.settings", ct);

    private async Task<bool> HasPlan(Guid shopId, CancellationToken ct) => await entitlements.Has(await branches.HeadOf(shopId, ct), "shop", PlanFeatures.PayLater, ct);

    public async Task<Result<ConsentResponse>> Handle(GrantConsent c, CancellationToken ct)
    {
        var r = c.Request;
        if (!await MayManage(r.PersonId, c.ShopId, ct)) return Result<ConsentResponse>.Fail("permission.denied", "Only the owner can share your sales history.");
        if (!await HasPlan(c.ShopId, ct)) return Result<ConsentResponse>.Fail("plan.feature_not_included", "Pay-later terms are not part of your plan.");
        var partner = Partner(r.PartnerCode);
        if (partner is null) return Result<ConsentResponse>.Fail("consent.partner_unknown", "We do not work with that finance partner.");

        var existing = await store.ActiveConsent(c.ShopId, partner.Code, ct);
        if (existing is not null) return Result<ConsentResponse>.Ok(FinanceMapping.ToResponse(existing));
        var consent = CreditConsent.Grant(c.ShopId, partner.Code, r.Months, r.PersonId, clock.GetUtcNow());
        if (!consent.IsSuccess) return Result<ConsentResponse>.Fail(consent.Error!.Code, consent.Error.Message);
        store.Add(consent.Value!);
        await store.Save(ct);
        return Result<ConsentResponse>.Ok(FinanceMapping.ToResponse(consent.Value!));
    }

    /// <summary>Takes effect at once: nothing more is shared after this, because every share checks the consent first.</summary>
    public async Task<Result<ConsentResponse>> Handle(WithdrawConsent c, CancellationToken ct)
    {
        if (!await MayManage(c.PersonId, c.ShopId, ct)) return Result<ConsentResponse>.Fail("permission.denied", "Only the owner can withdraw consent.");
        var consent = await store.Consent(c.ShopId, c.ConsentId, ct);
        if (consent is null) return Result<ConsentResponse>.Fail("consent.not_found", "Consent not found.");
        var done = consent.Withdraw(clock.GetUtcNow());
        if (!done.IsSuccess) return Result<ConsentResponse>.Fail(done.Error!.Code, done.Error.Message);
        await store.Save(ct);
        return Result<ConsentResponse>.Ok(FinanceMapping.ToResponse(consent));
    }

    public async Task<Result<ApplicationResponse>> Handle(ApplyForCredit c, CancellationToken ct)
    {
        var r = c.Request;
        if (!await MayManage(r.PersonId, c.ShopId, ct)) return Result<ApplicationResponse>.Fail("permission.denied", "Only the owner can apply for credit.");
        var existing = await store.Application(r.Id, ct);
        if (existing is not null) return existing.ShopId == c.ShopId ? Result<ApplicationResponse>.Ok(FinanceMapping.ToResponse(existing, 0)) : Result<ApplicationResponse>.Fail("application.not_found", "Application not found.");

        var consent = await store.Consent(c.ShopId, r.ConsentId, ct);
        if (consent is null) return Result<ApplicationResponse>.Fail("consent.not_found", "Consent not found.");
        var partner = Partner(consent.PartnerCode);
        if (partner is null) return Result<ApplicationResponse>.Fail("consent.partner_unknown", "We do not work with that finance partner.");
        var now = clock.GetUtcNow();
        var app = CreditApplication.Submit(r.Id, c.ShopId, consent, r.RequestedCents, r.PersonId, now);
        if (!app.IsSuccess) return Result<ApplicationResponse>.Fail(app.Error!.Code, app.Error.Message);

        // Share only what the consent says, only while it is active (checked above), and write down that it happened.
        var months = await history.Months(c.ShopId, consent.Months, ct);
        store.Add(new DataShareRecord(consent.Id, c.ShopId, consent.PartnerCode, consent.Months, months.Sum(m => m.SalesCents), now));
        var decision = await partner.Apply(new(r.Id, c.ShopId, r.RequestedCents, months), ct);
        if (!decision.IsSuccess)
        {
            await store.Save(ct);   // the share is kept on record even though nothing came back
            return Result<ApplicationResponse>.Fail("partner.unavailable", "The finance partner could not be reached. Try again soon.");
        }
        app.Value!.Decide(decision.Value!.Approved, decision.Value.Reference, decision.Value.ApprovedCents, decision.Value.TermDays, decision.Value.Reason, now);
        store.Add(app.Value);
        await store.Save(ct);
        return Result<ApplicationResponse>.Ok(FinanceMapping.ToResponse(app.Value, 0));
    }

    /// <summary>G4-2: pay a supplier order with the approved terms. The partner pays the supplier; the order shows as paid and the shop repays the partner.</summary>
    public async Task<Result<DrawResponse>> Handle(DrawOnCredit c, CancellationToken ct)
    {
        var r = c.Request;
        if (!await MayManage(r.PersonId, c.ShopId, ct)) return Result<DrawResponse>.Fail("permission.denied", "Only the owner can pay with credit terms.");
        var existing = await store.DrawForOrder(r.OrderId, ct);
        if (existing is not null) return existing.ShopId == c.ShopId ? Result<DrawResponse>.Ok(FinanceMapping.ToResponse(existing)) : Result<DrawResponse>.Fail("order.not_found", "Order not found.");

        var app = await store.Application(r.ApplicationId, ct);
        if (app is null || app.ShopId != c.ShopId) return Result<DrawResponse>.Fail("application.not_found", "Application not found.");
        if (app.Status != ApplicationStatus.Approved) return Result<DrawResponse>.Fail("credit.not_approved", "This application was not approved.");
        var partner = Partner(app.PartnerCode);
        if (partner is null) return Result<DrawResponse>.Fail("consent.partner_unknown", "We do not work with that finance partner.");

        var order = await orders.Get(r.OrderId, ct);
        if (order is null || order.ShopId != c.ShopId) return Result<DrawResponse>.Fail("order.not_found", "Order not found.");
        if (order.PaymentStatus != "owing") return Result<DrawResponse>.Fail("order.not_owing", "This order does not need paying.");

        var used = FinanceMapping.Used(await store.DrawsOf(app.Id, ct));
        if (order.TotalCents > app.ApprovedCents - used) return Result<DrawResponse>.Fail("credit.limit_exceeded", $"Only R{Math.Max(0, app.ApprovedCents - used) / 100} of your credit is available.");

        var paid = await partner.Disburse(app.PartnerReference!, order.OrderId, order.SupplierName, order.TotalCents, ct);
        if (!paid.IsSuccess) return Result<DrawResponse>.Fail("partner.unavailable", "The finance partner could not pay the supplier. Try again soon.");
        var draw = CreditDraw.Disburse(app, order.OrderId, order.SupplierName, order.TotalCents, paid.Value!.RepayCents, paid.Value.DueOn, paid.Value.Reference, r.PersonId, clock.GetUtcNow());
        store.Add(draw);
        await store.Save(ct);   // saved before the order is touched: the partner has paid, so we must remember it
        await orders.SetPaymentStatus(order.OrderId, "paid", ct);
        return Result<DrawResponse>.Ok(FinanceMapping.ToResponse(draw));
    }

    public async Task<Result<CreditSummaryResponse>> Handle(GetCreditSummary q, CancellationToken ct)
    {
        if (!await MayManage(q.PersonId, q.ShopId, ct)) return Result<CreditSummaryResponse>.Fail("permission.denied", "Only the owner can see credit.");
        var draws = await store.Draws(q.ShopId, ct);
        var apps = (await store.Applications(q.ShopId, ct)).OrderByDescending(a => a.CreatedAt).ToList();
        return Result<CreditSummaryResponse>.Ok(new(
            (await store.Consents(q.ShopId, ct)).OrderByDescending(c => c.GrantedAt).Select(FinanceMapping.ToResponse).ToList(),
            apps.Select(a => FinanceMapping.ToResponse(a, FinanceMapping.Used(draws.Where(d => d.ApplicationId == a.Id)))).ToList(),
            draws.OrderBy(d => d.DueOn).Select(FinanceMapping.ToResponse).ToList(),
            (await store.Shares(q.ShopId, 50, ct)).OrderByDescending(s => s.CreatedAt).Select(s => new DataShareResponse(s.CreatedAt, s.PartnerCode, s.Months, s.TotalSalesCents)).ToList()));
    }

    /// <summary>The partner tells us about a repayment. Signed; a repeated callback counts once.</summary>
    public async Task<Result<bool>> Handle(HandleFinanceCallback c, CancellationToken ct)
    {
        var partner = Partner(c.PartnerCode);
        if (partner is null) return Result<bool>.Fail("partner.not_found", "Unknown partner.");
        var now = clock.GetUtcNow();
        if (!partner.VerifyCallback(c.Signature, c.Body, now)) return Result<bool>.Fail("webhook.signature_invalid", "The signature does not match.");
        var parsed = partner.ParseRepayment(c.Body);
        if (!parsed.IsSuccess) return Result<bool>.Fail(parsed.Error!.Code, parsed.Error.Message);
        var p = parsed.Value!;
        if (await store.RepaymentExists(p.Reference, ct)) return Result<bool>.Ok(true);
        var draw = await store.DrawByReference(partner.Code, p.DrawReference, ct);
        if (draw is null) return Result<bool>.Fail("draw.not_found", "Unknown credit draw.");
        if (p.AmountCents <= 0) return Result<bool>.Fail("repayment.amount_invalid", "A repayment must be more than nothing.");

        store.Add(new CreditRepayment(draw.Id, p.AmountCents, p.Reference, p.PaidAt));
        draw.Repay(p.AmountCents);
        await store.Save(ct);
        return Result<bool>.Ok(true);
    }
}
