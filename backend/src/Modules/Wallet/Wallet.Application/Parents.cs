using Identity.Contracts;
using TillKnowIT.Dispatching;
using TillKnowIT.Reporting;
using TillKnowIT.SharedKernel;
using Wallet.Contracts;
using Wallet.Domain;

namespace Wallet.Application;

internal static class WalletAccess
{
    /// <summary>The wallet, if the caller is a linked parent of its holder or an admin of its school.</summary>
    public static async Task<Result<(SchoolWallet Wallet, Holder Holder, Guardianship? Guardian)>> Open(IWalletStore store, IPermissionChecker permissions, Guid walletId, Guid person, CancellationToken ct)
    {
        var wallet = await store.GetWallet(walletId, ct);
        if (wallet is null) return Result<(SchoolWallet, Holder, Guardianship?)>.Fail("wallet.not_found", "Wallet not found.");
        var holder = (await store.GetHolder(wallet.HolderId, ct))!;
        var guardian = await store.GuardianshipOf(person, holder.Id, ct);
        if (guardian is null)
        {
            var admin = await SchoolAccess.Admin(store, permissions, wallet.SchoolId, person, ct);
            if (!admin.IsSuccess) return Result<(SchoolWallet, Holder, Guardianship?)>.Fail("permission.denied", "You do not have access to this wallet.");
        }
        return Result<(SchoolWallet, Holder, Guardianship?)>.Ok((wallet, holder, guardian));
    }

    /// <summary>Only a parent, not a school admin: for things that move or limit a child's money.</summary>
    public static async Task<Result<(SchoolWallet Wallet, Holder Holder, Guardianship Guardian)>> Parent(IWalletStore store, Guid walletId, Guid person, CancellationToken ct)
    {
        var wallet = await store.GetWallet(walletId, ct);
        if (wallet is null) return Result<(SchoolWallet, Holder, Guardianship)>.Fail("wallet.not_found", "Wallet not found.");
        var guardian = await store.GuardianshipOf(person, wallet.HolderId, ct);
        if (guardian is null) return Result<(SchoolWallet, Holder, Guardianship)>.Fail("permission.denied", "You are not linked to this child.");
        return Result<(SchoolWallet, Holder, Guardianship)>.Ok((wallet, (await store.GetHolder(wallet.HolderId, ct))!, guardian));
    }

    public static async Task<long> SpentToday(IWalletStore store, Guid walletId, DateTimeOffset now, CancellationToken ct)
    {
        var day = BusinessDay.Of(now);
        return await store.SpentBetween(walletId, BusinessDay.Start(day), BusinessDay.Start(day.AddDays(1)), ct);
    }
}

// ---------------------------------------------------------------- W2-1: a parent links a child

public sealed record LinkChild(LinkChildRequest Request) : ICommand<ChildResponse>;
public sealed record GetMyChildren(Guid ParentPersonId) : IQuery<List<ChildResponse>>;

internal sealed class GuardianHandlers(IWalletStore store, IPersonDirectory people, TimeProvider clock)
    : ICommandHandler<LinkChild, ChildResponse>, IQueryHandler<GetMyChildren, List<ChildResponse>>
{
    public async Task<Result<ChildResponse>> Handle(LinkChild c, CancellationToken ct)
    {
        var r = c.Request;
        // Consent comes first: nothing about the child is shown, or even confirmed to exist, before it is given.
        if (!r.Consent || string.IsNullOrWhiteSpace(r.ConsentVersion)) return Result<ChildResponse>.Fail("consent.required", "You need to agree to how your child's data is used before you can link them.");
        if (await people.Get(r.ParentPersonId, ct) is null) return Result<ChildResponse>.Fail("person.not_found", "Sign up first.");

        var code = LinkCodes.Normalise(r.LinkCode);
        var holder = code.Length == 8 ? await store.HolderByLinkCode(LinkCodes.Hash(code), ct) : null;
        if (holder is null || holder.Status != HolderStatus.Active) return Result<ChildResponse>.Fail("link.code_invalid", "That code is not valid. Check it against the card from the school.");
        var school = (await store.GetSchool(holder.SchoolId, ct))!;
        if (!school.IsTrading) return Result<ChildResponse>.Fail("link.school_not_active", "This school is not ready yet.");

        if (await store.GuardianshipOf(r.ParentPersonId, holder.Id, ct) is not null) return Result<ChildResponse>.Fail("link.already_linked", "You are already linked to this child.");
        if ((await store.GuardiansOf(holder.Id, ct)).Count >= Holder.MaxGuardians) return Result<ChildResponse>.Fail("link.too_many_guardians", "This child already has the most parents linked. Ask the school.");

        await store.Add(new Guardianship(r.ParentPersonId, holder.Id, r.ConsentVersion.Trim(), clock.GetUtcNow()), ct);
        await store.Save(ct);
        return Result<ChildResponse>.Ok(await Child(holder, school, ct));
    }

    public async Task<Result<List<ChildResponse>>> Handle(GetMyChildren q, CancellationToken ct)
    {
        var list = new List<ChildResponse>();
        foreach (var g in await store.GuardianshipsOfParent(q.ParentPersonId, ct))
        {
            var holder = await store.GetHolder(g.HolderId, ct);
            if (holder is null || holder.Status == HolderStatus.Erased) continue;
            list.Add(await Child(holder, (await store.GetSchool(holder.SchoolId, ct))!, ct));
        }
        return Result<List<ChildResponse>>.Ok(list);
    }

    private async Task<ChildResponse> Child(Holder holder, School school, CancellationToken ct)
    {
        var wallet = (await store.WalletOfHolder(holder.Id, ct))!;
        return new ChildResponse(holder.Id, wallet.Id, holder.DisplayName, holder.Grade, holder.ClassName, school.Name, await store.Balance(wallet.AccountId, ct), wallet.DailyLimitCents, wallet.Status.ToString().ToLowerInvariant());
    }
}

// ---------------------------------------------------------------- W2-3 and W2-4: the wallet, limits, blocked items, statement

public sealed record GetWallet(Guid WalletId, Guid CallerPersonId) : IQuery<WalletResponse>;
public sealed record SetWalletLimits(Guid WalletId, SetLimitsRequest Request) : ICommand<WalletResponse>;
public sealed record SetWalletRestrictions(Guid WalletId, SetRestrictionsRequest Request) : ICommand<WalletResponse>;
public sealed record GetWalletStatement(Guid WalletId, Guid CallerPersonId, DateOnly? From, DateOnly? To) : IQuery<List<StatementLine>>;
public sealed record ExportWalletStatement(Guid WalletId, Guid CallerPersonId, DateOnly? From, DateOnly? To, string? Format) : IQuery<ReportFile>;
public sealed record BlockMyChildsCards(Guid HolderId, Guid ParentPersonId) : ICommand<int>;

internal sealed class WalletHandlers(IWalletStore store, IPermissionChecker permissions, TimeProvider clock)
    : IQueryHandler<GetWallet, WalletResponse>, ICommandHandler<SetWalletLimits, WalletResponse>, ICommandHandler<SetWalletRestrictions, WalletResponse>,
      IQueryHandler<GetWalletStatement, List<StatementLine>>, IQueryHandler<ExportWalletStatement, ReportFile>, ICommandHandler<BlockMyChildsCards, int>
{
    public async Task<Result<WalletResponse>> Handle(GetWallet q, CancellationToken ct)
    {
        var opened = await WalletAccess.Open(store, permissions, q.WalletId, q.CallerPersonId, ct);
        return opened.IsSuccess ? Result<WalletResponse>.Ok(await Respond(opened.Value.Wallet, opened.Value.Holder, opened.Value.Guardian, ct)) : Result<WalletResponse>.Fail(opened.Error!.Code, opened.Error.Message);
    }

    public async Task<Result<WalletResponse>> Handle(SetWalletLimits c, CancellationToken ct)
    {
        var opened = await WalletAccess.Parent(store, c.WalletId, c.Request.CallerPersonId, ct);
        if (!opened.IsSuccess) return Result<WalletResponse>.Fail(opened.Error!.Code, opened.Error.Message);
        var (wallet, holder, guardian) = opened.Value;

        var limit = wallet.SetDailyLimit(c.Request.DailyLimitCents);
        if (!limit.IsSuccess) return Result<WalletResponse>.Fail(limit.Error!.Code, limit.Error.Message);
        var alerts = guardian.SetAlerts(c.Request.NotifyOnPurchase, c.Request.LowBalanceAlertCents, c.Request.ClearLowBalanceAlert);
        if (!alerts.IsSuccess) return Result<WalletResponse>.Fail(alerts.Error!.Code, alerts.Error.Message);

        await store.Save(ct);
        return Result<WalletResponse>.Ok(await Respond(wallet, holder, guardian, ct));
    }

    public async Task<Result<WalletResponse>> Handle(SetWalletRestrictions c, CancellationToken ct)
    {
        var opened = await WalletAccess.Parent(store, c.WalletId, c.Request.CallerPersonId, ct);
        if (!opened.IsSuccess) return Result<WalletResponse>.Fail(opened.Error!.Code, opened.Error.Message);
        var (wallet, holder, guardian) = opened.Value;

        var list = new List<WalletRestriction>();
        foreach (var r in c.Request.Restrictions ?? [])
        {
            if (!Enum.TryParse<RestrictionKind>(r.Kind, ignoreCase: true, out var kind)) return Result<WalletResponse>.Fail("restriction.kind_invalid", "Kind is product or category.");
            if (string.IsNullOrWhiteSpace(r.Key) || r.Key.Length > 100) return Result<WalletResponse>.Fail("restriction.key_invalid", "Say which product or category to block.");
            if (!list.Any(x => x.Kind == kind && x.Key == r.Key.Trim().ToLowerInvariant())) list.Add(new WalletRestriction(wallet.Id, kind, r.Key, clock.GetUtcNow()));
        }
        if (list.Count > 200) return Result<WalletResponse>.Fail("restriction.too_many", "That is too many blocked items.");

        await store.ReplaceRestrictions(wallet.Id, list, ct);
        wallet.NoteChange();   // the tills pick the change up at their next sync
        await store.Save(ct);
        return Result<WalletResponse>.Ok(await Respond(wallet, holder, guardian, ct));
    }

    /// <summary>W3-3: a parent blocks every card on their child's wallet in one go; a replacement can then be linked to the same wallet.</summary>
    public async Task<Result<int>> Handle(BlockMyChildsCards c, CancellationToken ct)
    {
        if (await store.GuardianshipOf(c.ParentPersonId, c.HolderId, ct) is null) return Result<int>.Fail("permission.denied", "You are not linked to this child.");
        var now = clock.GetUtcNow();
        var active = (await store.TokensOf(c.HolderId, ct)).Where(t => t.Status == TokenStatus.Active).ToList();
        foreach (var t in active) t.Block(now);
        await store.Save(ct);
        return Result<int>.Ok(active.Count);
    }

    public async Task<Result<List<StatementLine>>> Handle(GetWalletStatement q, CancellationToken ct)
    {
        var opened = await WalletAccess.Open(store, permissions, q.WalletId, q.CallerPersonId, ct);
        if (!opened.IsSuccess) return Result<List<StatementLine>>.Fail(opened.Error!.Code, opened.Error.Message);
        var range = Range(q.From, q.To);
        if (!range.IsSuccess) return Result<List<StatementLine>>.Fail(range.Error!.Code, range.Error.Message);
        return Result<List<StatementLine>>.Ok(await Lines(opened.Value.Wallet, range.Value.From, range.Value.To, ct));
    }

    public async Task<Result<ReportFile>> Handle(ExportWalletStatement q, CancellationToken ct)
    {
        var opened = await WalletAccess.Open(store, permissions, q.WalletId, q.CallerPersonId, ct);
        if (!opened.IsSuccess) return Result<ReportFile>.Fail(opened.Error!.Code, opened.Error.Message);
        if (!ReportFormats.TryParse(q.Format, out var format)) return Result<ReportFile>.Fail("report.format_invalid", "Format must be csv, xlsx or pdf.");
        var range = Range(q.From, q.To);
        if (!range.IsSuccess) return Result<ReportFile>.Fail(range.Error!.Code, range.Error.Message);

        var lines = await Lines(opened.Value.Wallet, range.Value.From, range.Value.To, ct);
        var table = new ReportTable($"Wallet statement for {opened.Value.Holder.DisplayName}",
            [new("Date", ColumnKind.DateTime), new("What"), new("Amount", ColumnKind.Money), new("Balance", ColumnKind.Money)],
            lines.Select(l => new object?[] { l.At, l.Description, l.AmountCents, l.BalanceAfterCents }).ToList())
        {
            Notes = [$"Period: {range.Value.FromDay:yyyy-MM-dd} to {range.Value.ToDay:yyyy-MM-dd}", $"Closing balance: R {CellText.Rand(lines.LastOrDefault()?.BalanceAfterCents ?? 0)}"],
        };
        return Result<ReportFile>.Ok(ReportExporter.Export(table, format, $"wallet-statement-{range.Value.FromDay:yyyyMMdd}-{range.Value.ToDay:yyyyMMdd}"));
    }

    private Result<(DateOnly FromDay, DateOnly ToDay, DateTimeOffset From, DateTimeOffset To)> Range(DateOnly? from, DateOnly? to)
    {
        var toDay = to ?? BusinessDay.Of(clock.GetUtcNow());
        var fromDay = from ?? toDay.AddDays(-89);
        if (fromDay > toDay) return Result<(DateOnly, DateOnly, DateTimeOffset, DateTimeOffset)>.Fail("report.range_invalid", "The start date is after the end date.");
        if (toDay.DayNumber - fromDay.DayNumber > 366) return Result<(DateOnly, DateOnly, DateTimeOffset, DateTimeOffset)>.Fail("report.range_too_large", "A statement covers at most a year.");
        return Result<(DateOnly, DateOnly, DateTimeOffset, DateTimeOffset)>.Ok((fromDay, toDay, BusinessDay.Start(fromDay), BusinessDay.Start(toDay.AddDays(1))));
    }

    /// <summary>The running balance is built from the first entry, so it is right even when the period starts part-way through the history.</summary>
    private async Task<List<StatementLine>> Lines(SchoolWallet wallet, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        long running = 0;
        var lines = new List<StatementLine>();
        foreach (var e in (await store.EntriesOf(wallet.AccountId, null, to, ct)).OrderBy(e => e.At).ThenByDescending(e => e.AmountCents).ThenBy(e => e.TransactionId))
        {
            running += e.AmountCents;
            if (e.At >= from) lines.Add(new StatementLine(e.At, e.Type.ToString().ToLowerInvariant(), e.Description ?? Describe(e.Type), e.AmountCents, running));
        }
        return lines;
    }

    private static string Describe(LedgerTransactionType type) => type switch
    {
        LedgerTransactionType.TopUp => "Top-up",
        LedgerTransactionType.CashTopUp => "Cash top-up at the school office",
        LedgerTransactionType.Purchase => "Tuckshop purchase",
        LedgerTransactionType.PurchaseReversal => "Cancelled order refunded",
        LedgerTransactionType.Transfer => "Transfer between children",
        LedgerTransactionType.Refund => "Refund to you",
        _ => type.ToString(),
    };

    private async Task<WalletResponse> Respond(SchoolWallet wallet, Holder holder, Guardianship? guardian, CancellationToken ct) => new(
        wallet.Id, holder.Id, holder.DisplayName, await store.Balance(wallet.AccountId, ct), wallet.DailyLimitCents,
        await WalletAccess.SpentToday(store, wallet.Id, clock.GetUtcNow(), ct), wallet.Status.ToString().ToLowerInvariant(),
        (await store.Restrictions(wallet.Id, ct)).Select(r => new RestrictionDto(r.Kind.ToString().ToLowerInvariant(), r.Key)).ToList(),
        guardian?.NotifyOnPurchase ?? false, guardian?.LowBalanceAlertCents);
}
