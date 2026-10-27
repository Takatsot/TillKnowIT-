using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.Dispatching;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Credit;

public interface ICreditReadStore
{
    /// <summary>Every customer of the shop with the sum of their entries.</summary>
    Task<List<(Guid Id, string Name, string? Phone, long? LimitCents, long BalanceCents)>> Balances(Guid shopId, CancellationToken ct);

    Task<bool> AccountExists(Guid shopId, Guid accountId, CancellationToken ct);
    Task<List<CreditHistoryEntry>> History(Guid accountId, CancellationToken ct);
}

/// <summary>E6-1: who owes what. Customers who owe the most come first.</summary>
public sealed record ListCreditAccounts(Guid ShopId) : IQuery<List<CreditAccountBalance>>;

internal sealed class ListCreditAccountsHandler(ICreditReadStore store) : IQueryHandler<ListCreditAccounts, List<CreditAccountBalance>>
{
    public async Task<Result<List<CreditAccountBalance>>> Handle(ListCreditAccounts query, CancellationToken ct)
    {
        var rows = await store.Balances(query.ShopId, ct);
        return Result<List<CreditAccountBalance>>.Ok(rows
            .Select(r => new CreditAccountBalance(r.Id, r.Name, r.Phone, r.LimitCents, r.BalanceCents, CreditBook.OverLimit(r.BalanceCents, r.LimitCents)))
            .OrderByDescending(a => a.BalanceCents).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }
}

/// <summary>History of one customer: sales, payments and cancellations, newest first.</summary>
public sealed record GetCreditHistory(Guid ShopId, Guid AccountId) : IQuery<List<CreditHistoryEntry>>;

internal sealed class GetCreditHistoryHandler(ICreditReadStore store) : IQueryHandler<GetCreditHistory, List<CreditHistoryEntry>>
{
    public async Task<Result<List<CreditHistoryEntry>>> Handle(GetCreditHistory query, CancellationToken ct)
    {
        if (!await store.AccountExists(query.ShopId, query.AccountId, ct))
            return Result<List<CreditHistoryEntry>>.Fail("account.not_found", "Customer not found.");
        var history = await store.History(query.AccountId, ct);
        return Result<List<CreditHistoryEntry>>.Ok(history.OrderByDescending(h => h.CreatedAt).ToList());
    }
}
