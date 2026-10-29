using Identity.Contracts;
using Wallet.Contracts;
using Wallet.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace Wallet.Application;

public interface IWalletListings
{
    Task<List<WalletReconciliation>> Reconciliations(int take, CancellationToken ct);
}

public sealed record ListSchools(Guid CallerPersonId) : IQuery<List<SchoolResponse>>;
public sealed record ListReconciliations(Guid CallerPersonId) : IQuery<List<ReconciliationResponse>>;
public sealed record ListHolderTokens(Guid HolderId, Guid CallerPersonId) : IQuery<List<TokenResponse>>;

internal sealed class WalletListHandlers(IWalletStore store, IWalletListings listings, IPermissionChecker permissions)
    : IQueryHandler<ListSchools, List<SchoolResponse>>, IQueryHandler<ListReconciliations, List<ReconciliationResponse>>, IQueryHandler<ListHolderTokens, List<TokenResponse>>
{
    public async Task<Result<List<SchoolResponse>>> Handle(ListSchools q, CancellationToken ct) =>
        await permissions.Has(q.CallerPersonId, "school.manage", ct)
            ? Result<List<SchoolResponse>>.Ok((await store.AllSchools(ct)).OrderBy(s => s.Name).Select(SchoolMapping.ToResponse).ToList())
            : Result<List<SchoolResponse>>.Fail("permission.denied", "Only a platform admin can see every school.");

    public async Task<Result<List<ReconciliationResponse>>> Handle(ListReconciliations q, CancellationToken ct) =>
        await permissions.Has(q.CallerPersonId, "wallet.admin", ct)
            ? Result<List<ReconciliationResponse>>.Ok((await listings.Reconciliations(60, ct)).Select(r => new ReconciliationResponse(r.Id, r.Day, r.PartnerTotalCents, r.LedgerTotalCents, r.DifferenceCents, r.Status.ToString().ToLowerInvariant(), r.Note)).ToList())
            : Result<List<ReconciliationResponse>>.Fail("permission.denied", "Only a platform admin can see reconciliations.");

    /// <summary>A parent of the child sees the child's cards (never the secret behind them) so a lost one can be blocked; so does the school's admin.</summary>
    public async Task<Result<List<TokenResponse>>> Handle(ListHolderTokens q, CancellationToken ct)
    {
        var holder = await store.GetHolder(q.HolderId, ct);
        if (holder is null) return Result<List<TokenResponse>>.Fail("holder.not_found", "Child not found.");
        var allowed = await store.GuardianshipOf(q.CallerPersonId, holder.Id, ct) is not null ? Result.Ok() : await SchoolAccess.Admin(store, permissions, holder.SchoolId, q.CallerPersonId, ct);
        if (!allowed.IsSuccess) return Result<List<TokenResponse>>.Fail(allowed.Error!.Code, allowed.Error.Message);
        return Result<List<TokenResponse>>.Ok((await store.TokensOf(holder.Id, ct)).OrderBy(t => t.CreatedAt)
            .Select(t => new TokenResponse(t.Id, holder.Id, t.Type == TokenType.Nfc ? "nfc" : "qr", t.Status == TokenStatus.Active ? "active" : "blocked")).ToList());
    }
}
