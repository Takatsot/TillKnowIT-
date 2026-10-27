using System.Text.Json;
using Identity.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application;

internal static class TopUpMapping
{
    public static TopUpResponse ToResponse(TopUp t) => new(t.Id, t.WalletId, t.AmountCents, t.Status.ToString().ToLowerInvariant(), t.Status == TopUpStatus.Pending ? t.CheckoutUrl : null);
}

// ---------------------------------------------------------------- W2-2: card or instant EFT through the partner

public sealed record StartTopUp(Guid WalletId, StartTopUpRequest Request) : ICommand<TopUpResponse>;

internal sealed class StartTopUpHandler(IWalletStore store, IStoredValuePartner partner, TimeProvider clock) : ICommandHandler<StartTopUp, TopUpResponse>
{
    public async Task<Result<TopUpResponse>> Handle(StartTopUp c, CancellationToken ct)
    {
        var opened = await WalletAccess.Parent(store, c.WalletId, c.Request.CallerPersonId, ct);
        if (!opened.IsSuccess) return Result<TopUpResponse>.Fail(opened.Error!.Code, opened.Error.Message);
        var (wallet, holder, _) = opened.Value;
        if (wallet.Status != WalletStatus.Active) return Result<TopUpResponse>.Fail("wallet.frozen", "This wallet is not active.");
        if (holder.Status != HolderStatus.Active) return Result<TopUpResponse>.Fail("holder.not_active", "This child is no longer at the school.");

        var started = TopUp.Start(wallet.Id, c.Request.CallerPersonId, c.Request.AmountCents, clock.GetUtcNow());
        if (!started.IsSuccess) return Result<TopUpResponse>.Fail(started.Error!.Code, started.Error.Message);

        var checkout = await partner.CreateTopUpCheckout(new TopUpCheckoutRequest(started.Value!.Id.ToString("N"), c.Request.AmountCents, $"Wallet top-up for {holder.FirstName}", c.Request.ReturnUrl), ct);
        if (!checkout.IsSuccess) return Result<TopUpResponse>.Fail(checkout.Error!.Code, checkout.Error.Message);

        started.Value.SetCheckout(checkout.Value!.PartnerRef, checkout.Value.Url);
        await store.Add(started.Value, ct);
        await store.Save(ct);
        return Result<TopUpResponse>.Ok(TopUpMapping.ToResponse(started.Value));
    }
}

/// <summary>The partner's callback. Verified by signature, handled once per event id, and the wallet is credited only here.</summary>
public sealed record HandleWalletWebhook(string Partner, string? Signature, string Body) : ICommand<Unit>;

internal sealed class WalletWebhookHandler(IWalletWebhookVerifier verifier, IWalletStore store, IWalletNotifier notifier, TimeProvider clock) : ICommandHandler<HandleWalletWebhook, Unit>
{
    private sealed record Payload(string? Id, string? Type, string? PartnerRef, long? AmountCents);

    public async Task<Result<Unit>> Handle(HandleWalletWebhook c, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!verifier.Verify(c.Partner, c.Signature, c.Body, now)) return Result<Unit>.Fail("webhook.signature_invalid", "The callback could not be verified.");

        Payload? p;
        try { p = JsonSerializer.Deserialize<Payload>(c.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { p = null; }
        if (p is null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Type) || string.IsNullOrWhiteSpace(p.PartnerRef))
            return Result<Unit>.Fail("webhook.payload_invalid", "The callback is missing its id, type or reference.");
        if (await store.HasReceipt(c.Partner, p.Id, ct)) return Result<Unit>.Ok(default);

        var topUp = await store.TopUpByPartnerRef(p.PartnerRef, ct);
        if (topUp is null) return Result<Unit>.Fail("topup.unknown", "No top-up has that reference.");

        Result outcome = Result.Ok();
        var credited = false;
        switch (p.Type)
        {
            case "topup.completed":
                outcome = topUp.Complete(p.AmountCents ?? -1);
                if (outcome.IsSuccess && !await store.TransactionExists($"topup:{topUp.PartnerRef}", ct))
                {
                    var txn = LedgerTransaction.Create(LedgerTransactionType.TopUp, $"topup:{topUp.PartnerRef}", Postings.TopUp(topUp.WalletId, topUp.AmountCents), now, topUp.PartnerRef, "Top-up");
                    await store.Add(txn.Value!, ct);
                    credited = true;
                }
                break;
            case "topup.failed":
                topUp.Fail();
                break;
        }

        await store.Add(new WalletWebhookReceipt(c.Partner, p.Id, now), ct);
        await store.Save(ct);

        if (credited)
        {
            var wallet = (await store.GetWallet(topUp.WalletId, ct))!;
            var holder = (await store.GetHolder(wallet.HolderId, ct))!;
            await notifier.ToParent(topUp.RequestedBy, "wallet.topup", new() { ["child"] = holder.FirstName, ["amount"] = Rands.Rand(topUp.AmountCents), ["balance"] = Rands.Rand(await store.Balance(wallet.AccountId, ct)) },
                $"wallet:topup:{topUp.Id:N}", whatsapp: false, ct);
        }
        return outcome.IsSuccess ? Result<Unit>.Ok(default) : Result<Unit>.Fail(outcome.Error!.Code, outcome.Error.Message);
    }
}

// ---------------------------------------------------------------- W2-2: cash at the school office

public sealed record RecordCashTopUp(Guid SchoolId, CashTopUpRequest Request) : ICommand<Unit>;
public sealed record RecordCashDeposit(Guid SchoolId, Guid CallerPersonId, long AmountCents, string Reference) : ICommand<Unit>;

internal sealed class CashHandlers(IWalletStore store, IPermissionChecker permissions, TimeProvider clock)
    : ICommandHandler<RecordCashTopUp, Unit>, ICommandHandler<RecordCashDeposit, Unit>
{
    public async Task<Result<Unit>> Handle(RecordCashTopUp c, CancellationToken ct)
    {
        var r = c.Request;
        var school = await store.GetSchool(c.SchoolId, ct);
        if (school is null) return Result<Unit>.Fail("school.not_found", "School not found.");
        var allowed = await SchoolAccess.Admin(store, permissions, school.Id, r.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (!school.IsTrading) return Result<Unit>.Fail("school.not_active", "This school is not active yet.");
        if (r.AmountCents is < TopUp.MinCents or > TopUp.MaxCents) return Result<Unit>.Fail("topup.amount_invalid", "A top-up is from R20 to R5000.");
        if (string.IsNullOrWhiteSpace(r.Receipt) || r.Receipt.Length > 40) return Result<Unit>.Fail("topup.receipt_required", "Write the receipt number.");

        var wallet = await store.GetWallet(r.WalletId, ct);
        if (wallet is null || wallet.SchoolId != school.Id) return Result<Unit>.Fail("wallet.not_found", "Wallet not found.");
        if (wallet.Status != WalletStatus.Active) return Result<Unit>.Fail("wallet.frozen", "This wallet is not active.");

        // One receipt, one top-up: handing the same receipt in twice changes nothing.
        var reference = $"cash:{school.Id:N}:{r.Receipt.Trim().ToLowerInvariant()}";
        if (await store.TransactionExists(reference, ct)) return Result<Unit>.Ok(default);

        var txn = LedgerTransaction.Create(LedgerTransactionType.CashTopUp, reference, Postings.CashTopUp(school.Id, wallet.Id, r.AmountCents), clock.GetUtcNow(), description: $"Cash top-up, receipt {r.Receipt.Trim()}");
        await store.Add(txn.Value!, ct);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }

    /// <summary>The school has paid its collected cash in to the partner; the platform records it when the partner confirms.</summary>
    public async Task<Result<Unit>> Handle(RecordCashDeposit c, CancellationToken ct)
    {
        var allowed = await SchoolAccess.Platform(permissions, c.CallerPersonId, "wallet.admin", ct);
        if (!allowed.IsSuccess) return Result<Unit>.Fail(allowed.Error!.Code, allowed.Error.Message);
        if (await store.GetSchool(c.SchoolId, ct) is null) return Result<Unit>.Fail("school.not_found", "School not found.");
        if (c.AmountCents <= 0) return Result<Unit>.Fail("deposit.amount_invalid", "The amount must be more than zero.");
        if (string.IsNullOrWhiteSpace(c.Reference) || c.Reference.Length > 80) return Result<Unit>.Fail("deposit.reference_required", "Give the deposit reference.");

        var reference = $"deposit:{c.SchoolId:N}:{c.Reference.Trim().ToLowerInvariant()}";
        if (await store.TransactionExists(reference, ct)) return Result<Unit>.Ok(default);
        var txn = LedgerTransaction.Create(LedgerTransactionType.CashDeposit, reference, Postings.CashDeposit(c.SchoolId, c.AmountCents), clock.GetUtcNow(), description: $"Cash paid in, {c.Reference.Trim()}");
        await store.Add(txn.Value!, ct);
        await store.Save(ct);
        return Result<Unit>.Ok(default);
    }
}

// ---------------------------------------------------------------- W2-6: between a parent's own children

public sealed record TransferBetweenChildren(TransferRequest Request) : ICommand<Unit>;

internal sealed class TransferHandler(IWalletStore store, TimeProvider clock) : ICommandHandler<TransferBetweenChildren, Unit>
{
    public async Task<Result<Unit>> Handle(TransferBetweenChildren c, CancellationToken ct)
    {
        var r = c.Request;
        if (r.FromWalletId == r.ToWalletId) return Result<Unit>.Fail("transfer.same_wallet", "Choose two different children.");
        if (r.AmountCents <= 0 || r.AmountCents > TopUp.MaxCents) return Result<Unit>.Fail("transfer.amount_invalid", "The amount must be more than zero and at most R5000.");
        if (string.IsNullOrWhiteSpace(r.Reference) || r.Reference.Length > 60) return Result<Unit>.Fail("transfer.reference_required", "A transfer needs a reference so it cannot happen twice.");

        // The parent must be linked to both children: a transfer can never move money to someone else's child.
        var from = await WalletAccess.Parent(store, r.FromWalletId, r.CallerPersonId, ct);
        if (!from.IsSuccess) return Result<Unit>.Fail(from.Error!.Code, from.Error.Message);
        var to = await WalletAccess.Parent(store, r.ToWalletId, r.CallerPersonId, ct);
        if (!to.IsSuccess) return Result<Unit>.Fail(to.Error!.Code, to.Error.Message);
        if (from.Value.Wallet.Status != WalletStatus.Active || to.Value.Wallet.Status != WalletStatus.Active) return Result<Unit>.Fail("wallet.frozen", "Both wallets must be active.");

        var reference = $"transfer:{r.CallerPersonId:N}:{r.Reference.Trim().ToLowerInvariant()}";
        if (await store.TransactionExists(reference, ct)) return Result<Unit>.Ok(default);
        if (await store.Balance(from.Value.Wallet.AccountId, ct) < r.AmountCents) return Result<Unit>.Fail("wallet.insufficient", "Not enough money in that wallet.");

        var txn = LedgerTransaction.Create(LedgerTransactionType.Transfer, reference, Postings.Transfer(r.FromWalletId, r.ToWalletId, r.AmountCents), clock.GetUtcNow(),
            description: $"Transfer from {from.Value.Holder.FirstName} to {to.Value.Holder.FirstName}");
        await store.Add(txn.Value!, ct);
        from.Value.Wallet.NoteDebit();
        try { await store.Save(ct); }
        catch (ConcurrencyConflictException) { return Result<Unit>.Fail("wallet.busy", "The wallet was being used at the same moment. Try again."); }
        return Result<Unit>.Ok(default);
    }
}
