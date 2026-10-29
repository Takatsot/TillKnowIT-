using Identity.Contracts;
using ShopCore.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application;

public sealed record VoidWalletPaymentRequest(Guid DeviceId, Guid StaffId);

public sealed record VoidWalletPayment(Guid ShopId, Guid PurchaseId, VoidWalletPaymentRequest Request) : ICommand<Unit>;

/// <summary>
/// The cashier took a wallet payment and then abandoned the sale. Within half an hour, and only while the sale has not reached the server, the whole payment (fee included)
/// goes back to the wallet. Asking again changes nothing.
/// </summary>
internal sealed class VoidWalletPaymentHandler(IWalletStore store, ITillAccessChecker tills, ISaleLookup sales, TimeProvider clock) : ICommandHandler<VoidWalletPayment, Unit>
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    public async Task<Result<Unit>> Handle(VoidWalletPayment c, CancellationToken ct)
    {
        var access = await tills.Check(c.ShopId, c.Request.DeviceId, c.Request.StaffId, "sale.create", ct);
        if (!access.IsSuccess) return Result<Unit>.Fail(access.Error!.Code, access.Error.Message);
        var purchase = await store.GetPurchase(c.PurchaseId, ct);
        if (purchase is null || purchase.ShopId != c.ShopId) return Result<Unit>.Fail("purchase.not_found", "Payment not found.");
        if (purchase.ReversedAt is not null) return Result<Unit>.Ok(default);

        var now = clock.GetUtcNow();
        if (purchase.Offline || now - purchase.SoldAt > Window) return Result<Unit>.Fail("payment.too_late", "This payment can no longer be cancelled here. Ask the school or the parent to request a refund.");
        if (await sales.Recorded(purchase.Id, ct)) return Result<Unit>.Fail("payment.sale_recorded", "That sale was completed, so the payment stays. Refund the sale instead.");

        var legs = Postings.Reversal(purchase.WalletId, purchase.ShopId, purchase.AmountCents, purchase.FeeCents);
        var txn = LedgerTransaction.Create(LedgerTransactionType.PurchaseReversal, $"void:{purchase.Id:N}", legs, now, description: "Payment cancelled at the till");
        if (!txn.IsSuccess) return Result<Unit>.Fail(txn.Error!.Code, txn.Error.Message);
        await store.Add(txn.Value!, ct);
        purchase.Reverse(now);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}
