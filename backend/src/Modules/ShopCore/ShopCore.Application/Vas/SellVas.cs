using Identity.Contracts;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Vas;

public interface IVasStore
{
    Task<VasSale?> Get(Guid id, CancellationToken ct);
    Task Add(VasSale sale, CancellationToken ct);
    Task Save(CancellationToken ct);
}

/// <summary>What we ask the licensed partner to buy. Id is the same key we were given, so the partner can refuse a repeat too.</summary>
public sealed record VasPurchase(Guid Id, VasProductType Type, string Recipient, long AmountCents, string? ProductCode);

public enum VasOutcome { Success, Declined }

/// <summary>Declined means the partner answered no. If the partner cannot be reached the port throws, and the sale is marked unknown.</summary>
public sealed record VasPartnerResult(VasOutcome Outcome, string? PartnerRef = null, string? Token = null, string? Reason = null);

public interface IVasPartner
{
    Task<VasPartnerResult> Purchase(VasPurchase purchase, CancellationToken ct);
}

/// <summary>The commission rate per product in basis points. Placeholders until the partner agreement is signed; set from configuration.</summary>
public sealed record VasOptions(IReadOnlyDictionary<VasProductType, int> CommissionBps, TimeSpan? PartnerTimeout = null)
{
    public static VasOptions Default { get; } = new(Enum.GetValues<VasProductType>().ToDictionary(t => t, VasRules.DefaultCommissionBps));
    public int For(VasProductType type) => CommissionBps.TryGetValue(type, out var bps) ? bps : VasRules.DefaultCommissionBps(type);
}

/// <summary>E10-1: sell airtime, data or electricity. Needs the network, because the partner has to answer.</summary>
public sealed record SellVas(VasSaleRequest Request) : ICommand<VasSaleResponse>;

internal sealed class SellVasHandler(IVasStore store, ITillAccessChecker access, IEnumerable<IVasPartner> partners, VasOptions options, TimeProvider clock)
    : ICommandHandler<SellVas, VasSaleResponse>
{
    public async Task<Result<VasSaleResponse>> Handle(SellVas command, CancellationToken ct)
    {
        var r = command.Request;
        var allowed = await access.Check(r.ShopId, r.DeviceId, r.StaffId, "vas.sell", ct);
        if (!allowed.IsSuccess) return Result<VasSaleResponse>.Fail(allowed.Error!.Code, allowed.Error.Message);

        // A repeat of the same sale gets the same answer and never reaches the partner a second time.
        var existing = await store.Get(r.Id, ct);
        if (existing is not null)
            return existing.ShopId == r.ShopId
                ? Result<VasSaleResponse>.Ok(Respond(existing))
                : Result<VasSaleResponse>.Fail("vas.id_taken", "That sale id belongs to another shop.");

        var partner = partners.FirstOrDefault();
        if (partner is null)
            return Result<VasSaleResponse>.Fail("vas.unavailable", "Airtime and electricity are not available right now. Try again later.");

        if (!Enum.TryParse<VasProductType>(r.ProductType, ignoreCase: true, out var type) || !Enum.IsDefined(type))
            return Result<VasSaleResponse>.Fail("vas.product_invalid", "Choose airtime, data or electricity.");

        var created = VasSale.Create(new VasSaleInput(r.Id, r.ShopId, r.StaffId, r.DeviceId, type, r.Recipient, r.AmountCents, r.ProductCode, options.For(type), clock.GetUtcNow()));
        if (!created.IsSuccess) return Result<VasSaleResponse>.Fail(created.Error!.Code, created.Error.Message);
        var sale = created.Value!;

        // Saved BEFORE the partner is called: if we crash after they buy, the record is there to find, not a silent double sale.
        await store.Add(sale, ct);
        await store.Save(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.PartnerTimeout ?? TimeSpan.FromSeconds(30));
        try
        {
            var result = await partner.Purchase(new VasPurchase(sale.Id, type, sale.Recipient, sale.AmountCents, sale.ProductCode), timeout.Token);
            if (result.Outcome == VasOutcome.Success) sale.Complete(result.PartnerRef ?? sale.Id.ToString("N"), result.Token);
            else sale.Fail(result.Reason ?? "The partner declined the purchase.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // No answer (timeout, network, partner down): we cannot tell if it went through. Never guess and never retry.
            sale.MarkUnknown(ex is OperationCanceledException ? "The partner did not answer in time." : ex.Message);
        }

        await store.Save(ct);
        return Result<VasSaleResponse>.Ok(Respond(sale));
    }

    private static VasSaleResponse Respond(VasSale s) => new(s.Id, s.Status.ToString().ToLowerInvariant(), s.AmountCents, s.EarnedCommissionCents, s.PartnerRef, s.Token,
        s.Status switch
        {
            VasStatus.Failed => s.FailureReason,
            VasStatus.Unknown => "We could not confirm this sale. Do not sell it again; check with support first.",
            VasStatus.Pending => "This sale is still being processed.",
            _ => null,
        });
}
