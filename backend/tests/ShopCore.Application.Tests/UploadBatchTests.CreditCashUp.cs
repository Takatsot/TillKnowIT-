using Identity.Contracts;
using ShopCore.Application.Sync;
using ShopCore.Contracts;
using ShopCore.Domain;
using TillKnowIT.SharedKernel;

namespace ShopCore.Application.Tests;

public partial class UploadBatchTests
{
    private sealed partial class FakeStore
    {
        public Dictionary<Guid, CreditAccount> Accounts { get; } = [];
        public HashSet<Guid> CreditEntryIds { get; } = [];
        public HashSet<(Guid SaleId, CreditEntryType Type)> CreditKeys { get; } = [];
        public Dictionary<Guid, SaleSummary> Summaries { get; } = [];
        public HashSet<Guid> CashUpIds { get; } = [];
        public HashSet<(Guid Device, DateOnly Day)> CashUpDays { get; } = [];
        public List<CashSaleRow> StoredCashSales { get; } = [];
        public long StoredCreditCash { get; set; }
        public List<CreditAccount> AddedAccounts { get; } = [];
        public List<CreditEntry> AddedCreditEntries { get; } = [];
        public List<CashUp> AddedCashUps { get; } = [];

        public Task<Dictionary<Guid, CreditAccount>> GetCreditAccounts(IEnumerable<Guid> ids, CancellationToken ct) =>
            Task.FromResult(ids.Where(Accounts.ContainsKey).ToDictionary(i => i, i => Accounts[i]));
        public Task<HashSet<Guid>> ExistingCreditEntryIds(IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult(ids.Where(CreditEntryIds.Contains).ToHashSet());
        public Task<HashSet<(Guid SaleId, CreditEntryType Type)>> CreditEntryKeys(IEnumerable<Guid> saleIds, CancellationToken ct) =>
            Task.FromResult(CreditKeys.Where(k => saleIds.Contains(k.SaleId)).ToHashSet());
        public Task<Dictionary<Guid, SaleSummary>> SaleSummaries(Guid shopId, IEnumerable<Guid> saleIds, CancellationToken ct) =>
            Task.FromResult(saleIds.Where(Summaries.ContainsKey).ToDictionary(i => i, i => Summaries[i]));
        public Task<HashSet<Guid>> ExistingCashUpIds(IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult(ids.Where(CashUpIds.Contains).ToHashSet());
        public Task<bool> CashUpExists(Guid shopId, Guid deviceId, DateOnly day, CancellationToken ct) => Task.FromResult(CashUpDays.Contains((deviceId, day)));
        public Task<List<CashSaleRow>> CashSales(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => Task.FromResult(StoredCashSales.ToList());
        public Task<long> CreditCashPayments(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => Task.FromResult(StoredCreditCash);
        public void Add(CreditAccount account) => AddedAccounts.Add(account);
        public void Add(CreditEntry entry) => AddedCreditEntries.Add(entry);
        public void Add(CashUp cashUp) => AddedCashUps.Add(cashUp);
    }

    private Task<Result<SyncUploadResponse>> UploadAll(
        List<SaleUpload>? sales = null, List<StockMovementUpload>? moves = null, List<SaleCancellationUpload>? cancels = null,
        List<ProductUpload>? products = null, List<CreditAccountUpload>? accounts = null,
        List<CreditEntryUpload>? entries = null, List<CashUpUpload>? cashUps = null) =>
        new UploadBatchHandler(_store, _access, new FakeLookups(), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now))
            .Handle(new UploadBatch(new SyncUploadRequest(_shop, _device, sales, moves, cancels, products, accounts, entries, cashUps)), default);

    private CreditAccountUpload Customer(Guid? id = null, string name = "Mrs Dlamini", long? limit = 50000, DateTimeOffset? at = null) =>
        new(id ?? Guid.NewGuid(), _manager, name, "0821234567", limit, at ?? Now);

    private SaleUpload CreditSale(Guid accountId, Guid? id = null) => new(
        id ?? Guid.NewGuid(), _cashier, Now, "credit", 3600, 0, null, null, accountId, [new SaleLineUpload(_bread, "Bread", 2, 1800, 1400)]);

    private CreditEntryUpload SaleEntry(SaleUpload sale, long? amount = null, Guid? account = null) =>
        new(Guid.NewGuid(), _cashier, account ?? sale.AccountId!.Value, "sale", amount ?? sale.TotalCents, sale.Id, null, Now);

    private CreditEntryUpload Payment(Guid accountId, long amount = -1000, string method = "cash") =>
        new(Guid.NewGuid(), _cashier, accountId, "payment", amount, null, method, Now);

    // ---- credit customers ----

    [Fact]
    public async Task Customer_credit_sale_and_its_entry_can_arrive_together()
    {
        var customer = Customer();
        var sale = CreditSale(customer.Id);
        var entry = SaleEntry(sale);

        var result = await UploadAll([sale], accounts: [customer], entries: [entry]);

        Assert.All(result.Value!.Results, r => Assert.Equal("accepted", r.Status));
        Assert.Equal(["credit_account", "sale", "credit_entry"], result.Value.Results.Select(r => r.Kind));
        Assert.Equal(3600, _store.AddedCreditEntries.Single().AmountCents);
    }

    [Fact]
    public async Task Credit_sale_for_an_unknown_customer_is_rejected()
    {
        var result = await UploadAll([CreditSale(Guid.NewGuid())]);
        Assert.Equal("account.unknown", result.Value!.Results.Single().Code);
        Assert.Empty(_store.AddedSales);
    }

    [Fact]
    public async Task Customers_need_credit_manage_a_name_and_a_sensible_limit()
    {
        _access.Denied[(_manager, "credit.manage")] = Result.Fail("permission.denied", "no");
        Assert.Equal("permission.denied", (await UploadAll(accounts: [Customer()])).Value!.Results.Single().Code);

        _access.Denied.Clear();
        var result = await UploadAll(accounts: [Customer(name: " "), Customer(limit: -1), Customer(at: Now.AddDays(5))]);
        Assert.Equal(["account.name_invalid", "account.limit_invalid", "account.date_invalid"], result.Value!.Results.Select(r => r.Code));
    }

    [Fact]
    public async Task A_customer_without_a_limit_is_allowed()
    {
        var result = await UploadAll(accounts: [Customer(limit: null)]);
        Assert.Equal("accepted", result.Value!.Results.Single().Status);
    }

    [Fact]
    public async Task Editing_a_customer_keeps_the_latest_change()
    {
        var id = Guid.NewGuid();
        var existing = CreditAccount.Record(new CreditAccountInput(id, _shop, _manager, _device, "Mrs Dlamini", null, 10000, Now.AddHours(-2)));
        _store.Accounts[id] = existing;

        var newer = await UploadAll(accounts: [Customer(id, limit: 90000, at: Now.AddHours(-1))]);
        var older = await UploadAll(accounts: [Customer(id, limit: 1, at: Now.AddHours(-1.5))]);

        Assert.Equal("accepted", newer.Value!.Results.Single().Status);
        Assert.Equal("duplicate", older.Value!.Results.Single().Status);
        Assert.Equal(90000, existing.CreditLimitCents);
    }

    // ---- credit entries ----

    [Fact]
    public async Task Entry_amount_must_match_the_sale()
    {
        var customer = Customer();
        var sale = CreditSale(customer.Id);

        var result = await UploadAll([sale], accounts: [customer], entries: [SaleEntry(sale, amount: 100)]);

        Assert.Equal("credit.amount_mismatch", result.Value!.Results[2].Code);
        Assert.Empty(_store.AddedCreditEntries);
    }

    [Fact]
    public async Task Entry_must_use_the_sales_own_customer_and_a_credit_sale()
    {
        var customer = Customer();
        var other = Customer(name: "Someone else");
        var sale = CreditSale(customer.Id);
        var cash = CashSale();

        var result = await UploadAll([sale, cash], accounts: [customer, other], entries: [
            SaleEntry(sale, account: other.Id),
            new(Guid.NewGuid(), _cashier, customer.Id, "sale", cash.TotalCents, cash.Id, null, Now),
        ]);

        Assert.Equal("credit.account_mismatch", result.Value!.Results.Single(r => r.Kind == "credit_entry" && r.Code == "credit.account_mismatch").Code);
        Assert.Contains(result.Value.Results, r => r.Code == "credit.sale_unknown");
    }

    [Fact]
    public async Task A_sale_is_charged_to_the_customer_only_once()
    {
        var customer = Customer();
        var sale = CreditSale(customer.Id);

        var result = await UploadAll([sale], accounts: [customer], entries: [SaleEntry(sale), SaleEntry(sale)]);

        Assert.Equal("accepted", result.Value!.Results[2].Status);
        Assert.Equal("credit.already_recorded", result.Value.Results[3].Code);
        Assert.Single(_store.AddedCreditEntries);
    }

    [Fact]
    public async Task Cancelling_a_credit_sale_takes_it_off_the_customer_but_only_after_the_sale_is_cancelled()
    {
        var customer = Customer();
        var sale = CreditSale(customer.Id);
        var charge = SaleEntry(sale);
        var refund = new CreditEntryUpload(Guid.NewGuid(), _manager, customer.Id, "cancel", -sale.TotalCents, sale.Id, null, Now);

        var notYet = await UploadAll([sale], accounts: [customer], entries: [charge, refund]);
        Assert.Equal("credit.sale_not_cancelled", notYet.Value!.Results[3].Code);

        _store.AddedCreditEntries.Clear();
        var cancel = new SaleCancellationUpload(Guid.NewGuid(), _manager, sale.Id, "Wrong customer", Now);
        var sale2 = CreditSale(customer.Id);
        var charge2 = SaleEntry(sale2);
        var refund2 = new CreditEntryUpload(Guid.NewGuid(), _manager, customer.Id, "cancel", -sale2.TotalCents, sale2.Id, null, Now);
        var cancel2 = new SaleCancellationUpload(Guid.NewGuid(), _manager, sale2.Id, "Wrong customer", Now);

        var done = await UploadAll([sale2], cancels: [cancel2], accounts: [customer], entries: [charge2, refund2]);

        Assert.All(done.Value!.Results, r => Assert.NotEqual("rejected", r.Status));
        Assert.Equal(0, _store.AddedCreditEntries.Sum(e => e.AmountCents));
        _ = cancel;
    }

    [Fact]
    public async Task Customer_payments_are_negative_have_a_method_and_a_cashier_may_take_them()
    {
        var customer = Customer();
        _store.Accounts[customer.Id] = CreditAccount.Record(new CreditAccountInput(customer.Id, _shop, _manager, _device, "Mrs Dlamini", null, null, Now.AddDays(-1)));

        var good = await UploadAll(entries: [Payment(customer.Id), Payment(customer.Id, method: "card")]);
        Assert.All(good.Value!.Results, r => Assert.Equal("accepted", r.Status));

        var bad = await UploadAll(entries: [Payment(customer.Id, amount: 500), Payment(customer.Id, method: "credit"), Payment(customer.Id, amount: 0)]);
        Assert.Equal(["credit.amount_sign", "credit.payment_method", "credit.amount_zero"], bad.Value!.Results.Select(r => r.Code));
    }

    [Fact]
    public async Task Payments_need_the_credit_payment_permission_and_a_known_customer()
    {
        _access.Denied[(_cashier, "credit.payment")] = Result.Fail("permission.denied", "no");
        Assert.Equal("permission.denied", (await UploadAll(entries: [Payment(Guid.NewGuid())])).Value!.Results.Single().Code);

        _access.Denied.Clear();
        Assert.Equal("account.unknown", (await UploadAll(entries: [Payment(Guid.NewGuid())])).Value!.Results.Single().Code);
    }

    [Fact]
    public async Task Credit_balance_is_the_sum_of_entries_and_the_limit_only_warns()
    {
        Assert.Equal(2600, CreditBook.Balance([3600, -1000]));
        Assert.True(CreditBook.OverLimit(60000, 50000));
        Assert.False(CreditBook.OverLimit(50000, 50000));
        Assert.False(CreditBook.OverLimit(999999, null));
        await Task.CompletedTask;
    }

    // ---- cash-up ----

    private static readonly DateOnly Today = new(2026, 10, 26);

    private CashUpUpload CashUpFor(long expected, long counted = 0, long floatCents = 5000, DateOnly? day = null, DateTimeOffset? at = null) =>
        new(Guid.NewGuid(), _cashier, day ?? Today, floatCents, expected, counted, null, at ?? Now);

    [Fact]
    public async Task Cash_up_expected_is_float_plus_cash_sales_plus_cash_paid_on_credit()
    {
        _store.StoredCashSales.Add(new CashSaleRow(Guid.NewGuid(), 3600, false));
        _store.StoredCashSales.Add(new CashSaleRow(Guid.NewGuid(), 2500, false));
        _store.StoredCreditCash = 1000;

        var result = await UploadAll(cashUps: [CashUpFor(expected: 12100, counted: 12000)]);

        var outcome = result.Value!.Results.Single();
        Assert.Equal("accepted", outcome.Status);
        Assert.Null(outcome.Code);
        var saved = _store.AddedCashUps.Single();
        Assert.Equal(12100, saved.ExpectedCents);
        Assert.Equal(-100, saved.DifferenceCents);
        Assert.Equal(Today, saved.Day);
    }

    [Fact]
    public async Task Cancelled_cash_sales_are_not_in_expected_cash()
    {
        _store.StoredCashSales.Add(new CashSaleRow(Guid.NewGuid(), 3600, false));
        _store.StoredCashSales.Add(new CashSaleRow(Guid.NewGuid(), 9999, true));

        await UploadAll(cashUps: [CashUpFor(expected: 8600, counted: 8600)]);

        Assert.Equal(8600, _store.AddedCashUps.Single().ExpectedCents);
        Assert.Equal(0, _store.AddedCashUps.Single().DifferenceCents);
    }

    [Fact]
    public async Task Sales_and_cancellations_in_the_same_batch_are_counted_too()
    {
        var kept = CashSale();
        var cancelled = CashSale();
        var cancel = new SaleCancellationUpload(Guid.NewGuid(), _manager, cancelled.Id, "Mistake", Now);

        await UploadAll([kept, cancelled], cancels: [cancel], cashUps: [CashUpFor(expected: 8600)]);

        Assert.Equal(5000 + 3600, _store.AddedCashUps.Single().ExpectedCents);
    }

    [Fact]
    public async Task When_the_till_and_server_disagree_the_server_figure_is_kept_and_flagged()
    {
        _store.StoredCashSales.Add(new CashSaleRow(Guid.NewGuid(), 3600, false));

        var result = await UploadAll(cashUps: [CashUpFor(expected: 99999, counted: 8600)]);

        var outcome = result.Value!.Results.Single();
        Assert.Equal("accepted", outcome.Status);
        Assert.Equal("cashup.expected_adjusted", outcome.Code);
        Assert.Equal(8600, _store.AddedCashUps.Single().ExpectedCents);
    }

    [Fact]
    public async Task A_till_is_cashed_up_once_a_day()
    {
        _store.CashUpDays.Add((_device, Today));
        var already = await UploadAll(cashUps: [CashUpFor(5000)]);
        Assert.Equal("cashup.already_done", already.Value!.Results.Single().Code);

        _store.CashUpDays.Clear();
        var twice = await UploadAll(cashUps: [CashUpFor(5000), CashUpFor(5000)]);
        Assert.Equal(["accepted", "cashup.already_done"], twice.Value!.Results.Select(r => r.Code ?? r.Status));
    }

    [Fact]
    public async Task Cash_up_rules_day_amounts_permission_and_retries()
    {
        var wrongDay = await UploadAll(cashUps: [CashUpFor(5000, day: Today.AddDays(1))]);
        Assert.Equal("cashup.day_invalid", wrongDay.Value!.Results.Single().Code);

        var negative = await UploadAll(cashUps: [CashUpFor(5000, counted: -1)]);
        Assert.Equal("cashup.amount_invalid", negative.Value!.Results.Single().Code);

        var future = await UploadAll(cashUps: [CashUpFor(5000, day: Today.AddDays(9), at: Now.AddDays(9))]);
        Assert.Equal("cashup.date_invalid", future.Value!.Results.Single().Code);

        var done = CashUpFor(5000);
        _store.CashUpIds.Add(done.Id);
        Assert.Equal("duplicate", (await UploadAll(cashUps: [done])).Value!.Results.Single().Status);

        _access.Denied[(_cashier, "cashup.do")] = Result.Fail("permission.denied", "no");
        Assert.Equal("permission.denied", (await UploadAll(cashUps: [CashUpFor(5000)])).Value!.Results.Single().Code);
    }

    [Fact]
    public void Business_day_is_a_fixed_utc_plus_two_day()
    {
        Assert.Equal(new DateOnly(2026, 10, 26), BusinessDay.Of(new DateTimeOffset(2026, 10, 26, 21, 59, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 10, 27), BusinessDay.Of(new DateTimeOffset(2026, 10, 26, 22, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 22, 0, 0, TimeSpan.Zero), BusinessDay.Start(Today).ToUniversalTime());
        Assert.Equal(12100, CashUpCalculator.Expected(5000, 6100, 1000));
    }
}
