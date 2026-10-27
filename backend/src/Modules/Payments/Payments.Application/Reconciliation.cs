using System.Text.Json;
using Identity.Contracts;
using Payments.Contracts;
using Payments.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Payments.Application;

/// <summary>
/// M5: the daily check that our payments and the partner's statement agree to the cent. Anything the partner has that we do not,
/// anything we have that the partner does not, and any difference in amount or state is listed for a person to look at.
/// </summary>
public static class Reconciler
{
    public static ReconciliationResponse Compare(DateOnly day, IReadOnlyCollection<Payment> ours, IReadOnlyCollection<PartnerTransaction> theirs)
    {
        var mismatches = new List<ReconciliationMismatch>();
        var matched = 0;
        var partner = theirs.ToDictionary(t => t.PartnerRef);

        foreach (var p in ours.Where(p => p.PartnerRef is not null))
        {
            if (!partner.Remove(p.PartnerRef!, out var t))
            {
                mismatches.Add(new(p.PartnerRef!, "missing_at_partner", $"We show {p.Status.ToString().ToLowerInvariant()} for {p.AmountCents} cents; the partner has no such payment."));
                continue;
            }
            var state = p.Status.ToString().ToLowerInvariant();
            if (t.AmountCents != p.AmountCents) mismatches.Add(new(p.PartnerRef!, "amount", $"We have {p.AmountCents} cents, the partner has {t.AmountCents}."));
            else if (t.State != state) mismatches.Add(new(p.PartnerRef!, "state", $"We have {state}, the partner has {t.State}."));
            else if (t.ReleasedCents != p.ReleasedCents || t.RefundedCents != p.RefundedCents)
                mismatches.Add(new(p.PartnerRef!, "settlement", $"We released {p.ReleasedCents} and refunded {p.RefundedCents}; the partner shows {t.ReleasedCents} and {t.RefundedCents}."));
            else matched++;
        }
        foreach (var t in partner.Values) mismatches.Add(new(t.PartnerRef, "unknown_to_us", $"The partner shows {t.State} for {t.AmountCents} cents; we have no such payment."));
        return new ReconciliationResponse(day, matched, mismatches);
    }
}

public sealed record ReconcilePayments(DateOnly Day, Guid CallerPersonId) : ICommand<ReconciliationResponse>;

internal sealed class ReconcilePaymentsHandler(IPaymentStore payments, IPaymentPartner partner, IPermissionChecker permissions, TimeProvider clock)
    : ICommandHandler<ReconcilePayments, ReconciliationResponse>
{
    public async Task<Result<ReconciliationResponse>> Handle(ReconcilePayments c, CancellationToken ct)
    {
        if (!await permissions.Has(c.CallerPersonId, "dispute.resolve", ct)) return Result<ReconciliationResponse>.Fail("permission.denied", "Only a platform admin can reconcile payments.");

        var from = new DateTimeOffset(c.Day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = from.AddDays(1);
        var result = Reconciler.Compare(c.Day, await payments.ChangedBetween(from, to, ct), await partner.Statement(from, to, ct));

        await payments.Add(new ReconciliationRun(c.Day, result.Matched, JsonSerializer.Serialize(result.Mismatches), clock.GetUtcNow()), ct);
        await payments.Save(ct);
        return Result<ReconciliationResponse>.Ok(result);
    }
}
