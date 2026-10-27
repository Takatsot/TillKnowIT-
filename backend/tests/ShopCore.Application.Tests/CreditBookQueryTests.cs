using ShopCore.Application.Credit;
using ShopCore.Contracts;

namespace ShopCore.Application.Tests;

public class CreditBookQueryTests
{
    private sealed class FakeReadStore : ICreditReadStore
    {
        public List<(Guid Id, string Name, string? Phone, long? LimitCents, long BalanceCents)> Rows { get; } = [];
        public List<CreditHistoryEntry> Entries { get; } = [];
        public Guid KnownAccount { get; set; }

        public Task<List<(Guid Id, string Name, string? Phone, long? LimitCents, long BalanceCents)>> Balances(Guid shopId, CancellationToken ct) => Task.FromResult(Rows);
        public Task<bool> AccountExists(Guid shopId, Guid accountId, CancellationToken ct) => Task.FromResult(accountId == KnownAccount);
        public Task<List<CreditHistoryEntry>> History(Guid accountId, CancellationToken ct) => Task.FromResult(Entries);
    }

    [Fact]
    public async Task Lists_who_owes_what_biggest_debt_first_and_flags_those_over_their_limit()
    {
        var store = new FakeReadStore();
        store.Rows.Add((Guid.NewGuid(), "Zodwa", null, 50000, 20000));
        store.Rows.Add((Guid.NewGuid(), "Ayanda", "0821234567", 10000, 15000));
        store.Rows.Add((Guid.NewGuid(), "Bheki", null, null, 90000));
        store.Rows.Add((Guid.NewGuid(), "Cebo", null, 5000, 0));

        var result = await new ListCreditAccountsHandler(store).Handle(new ListCreditAccounts(Guid.NewGuid()), default);

        Assert.Equal(["Bheki", "Zodwa", "Ayanda", "Cebo"], result.Value!.Select(a => a.Name));
        Assert.Equal([false, false, true, false], result.Value!.Select(a => a.OverLimit));
    }

    [Fact]
    public async Task History_is_newest_first_and_an_unknown_customer_is_not_found()
    {
        var store = new FakeReadStore { KnownAccount = Guid.NewGuid() };
        var t = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
        store.Entries.Add(new CreditHistoryEntry(Guid.NewGuid(), "sale", 3600, Guid.NewGuid(), null, t));
        store.Entries.Add(new CreditHistoryEntry(Guid.NewGuid(), "payment", -1000, null, "cash", t.AddHours(2)));

        var found = await new GetCreditHistoryHandler(store).Handle(new GetCreditHistory(Guid.NewGuid(), store.KnownAccount), default);
        var missing = await new GetCreditHistoryHandler(store).Handle(new GetCreditHistory(Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.Equal(["payment", "sale"], found.Value!.Select(h => h.Type));
        Assert.Equal("account.not_found", missing.Error!.Code);
    }
}
