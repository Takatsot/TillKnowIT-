using TillKnowIT.SharedKernel;

namespace Accounting.Domain;

public static class Providers
{
    public const string Xero = "xero";
    public const string Sage = "sage";
    public static bool IsValid(string? p) => p is Xero or Sage;
}

/// <summary>The kinds of account the daily journal posts to. The owner maps each to an account in their own books.</summary>
public static class AccountKeys
{
    public const string Cash = "cash";
    public const string Card = "card_clearing";
    public const string CreditBook = "customer_credit";
    public const string OtherReceipts = "other_receipts";
    public const string Sales = "sales";
    public const string VatOutput = "vat_output";
    public const string CostOfSales = "cost_of_sales";
    public const string Stock = "stock";
    public const string Creditors = "creditors";

    public static readonly string[] All = [Cash, Card, CreditBook, OtherReceipts, Sales, VatOutput, CostOfSales, Stock, Creditors];
}

/// <summary>G2-1: a shop's link to an accounting package. Only a reference to the access token in the secrets store is kept here, never the token.</summary>
public sealed class AccountingConnection : VersionedEntity
{
    private AccountingConnection() { }

    public Guid ShopId { get; private set; }
    public string Provider { get; private set; } = default!;
    public string TokenReference { get; private set; } = default!;
    public string? TenantName { get; private set; }
    public DateOnly StartsOn { get; private set; }
    public DateTimeOffset ConnectedAt { get; private set; }
    public DateTimeOffset? DisconnectedAt { get; private set; }

    public bool IsConnected => DisconnectedAt is null;

    public static Result<AccountingConnection> Connect(Guid shopId, string? provider, string tokenReference, string? tenantName, Guid personId, DateOnly startsOn, DateTimeOffset now)
    {
        if (!Providers.IsValid(provider)) return Result<AccountingConnection>.Fail("accounting.provider_unknown", "Choose Xero or Sage.");
        if (string.IsNullOrWhiteSpace(tokenReference)) return Result<AccountingConnection>.Fail("accounting.token_missing", "The connection did not give us access.");
        var c = new AccountingConnection { ShopId = shopId, Provider = provider!, TokenReference = tokenReference, TenantName = tenantName, StartsOn = startsOn, ConnectedAt = now };
        c.StampFromDevice(Guid.CreateVersion7(), now, personId, null);
        return Result<AccountingConnection>.Ok(c);
    }

    public void Disconnect(DateTimeOffset now)
    {
        DisconnectedAt = now;
        Touch();
    }

    public void Reconnect(string provider, string tokenReference, string? tenantName, DateOnly startsOn, DateTimeOffset now)
    {
        Provider = provider;
        TokenReference = tokenReference;
        TenantName = tenantName;
        StartsOn = startsOn;
        ConnectedAt = now;
        DisconnectedAt = null;
        Touch();
    }
}

public sealed class AccountMapping : VersionedEntity
{
    private AccountMapping() { }

    public Guid ShopId { get; private set; }
    public string Key { get; private set; } = default!;
    public string AccountCode { get; private set; } = default!;

    public static Result<AccountMapping> Create(Guid shopId, string key, string? accountCode, DateTimeOffset now)
    {
        if (!AccountKeys.All.Contains(key)) return Result<AccountMapping>.Fail("mapping.key_unknown", $"'{key}' is not an account we post to.");
        if (string.IsNullOrWhiteSpace(accountCode) || accountCode.Length > 40) return Result<AccountMapping>.Fail("mapping.account_invalid", "Give the account code from your books.");
        var m = new AccountMapping { ShopId = shopId, Key = key, AccountCode = accountCode.Trim() };
        m.StampFromDevice(Guid.CreateVersion7(), now, null, null);
        return Result<AccountMapping>.Ok(m);
    }

    public void SetAccount(string accountCode)
    {
        AccountCode = accountCode.Trim();
        Touch();
    }
}

public sealed record JournalLine(string AccountKey, string AccountCode, long DebitCents, long CreditCents, string Narration);

/// <summary>One day's journal for the books. It always balances: debits equal credits.</summary>
public sealed record Journal(DateOnly Day, IReadOnlyList<JournalLine> Lines)
{
    public long Debits => Lines.Sum(l => l.DebitCents);
    public long Credits => Lines.Sum(l => l.CreditCents);
    public bool Balances => Debits == Credits;
}

public sealed record DayTotals(DateOnly Day, long CashCents, long CardCents, long CreditCents, long OtherCents, long VatCents, long CostOfSalesCents, long PurchasesCents)
{
    public long GrossCents => CashCents + CardCents + CreditCents + OtherCents;
}

public static class JournalBuilder
{
    /// <summary>
    /// Takings go on the debit side by how they were paid; the sales (before VAT) and the VAT go on the credit side. Cost of what was sold moves out of stock.
    /// Stock bought in the day goes into stock and is owed to suppliers. Lines with nothing in them are left out. Fails if an account is not mapped.
    /// </summary>
    public static Result<Journal> Build(DayTotals t, IReadOnlyDictionary<string, string> accounts)
    {
        if (new[] { t.CashCents, t.CardCents, t.CreditCents, t.OtherCents, t.VatCents, t.CostOfSalesCents, t.PurchasesCents }.Any(v => v < 0) || t.VatCents > t.GrossCents)
            return Result<Journal>.Fail("accounting.unbalanced", "The day's figures do not add up, so nothing was sent.");
        var lines = new List<JournalLine>();
        string? missing = null;
        void Add(string key, long debit, long credit, string narration)
        {
            if (debit == 0 && credit == 0) return;
            if (!accounts.TryGetValue(key, out var code)) { missing ??= key; return; }
            lines.Add(new(key, code, debit, credit, narration));
        }

        var day = t.Day.ToString("yyyy-MM-dd");
        Add(AccountKeys.Cash, t.CashCents, 0, $"Cash sales {day}");
        Add(AccountKeys.Card, t.CardCents, 0, $"Card and QR sales {day}");
        Add(AccountKeys.CreditBook, t.CreditCents, 0, $"Sales on credit {day}");
        Add(AccountKeys.OtherReceipts, t.OtherCents, 0, $"Other receipts {day}");
        Add(AccountKeys.Sales, 0, t.GrossCents - t.VatCents, $"Sales excluding VAT {day}");
        Add(AccountKeys.VatOutput, 0, t.VatCents, $"VAT on sales {day}");
        Add(AccountKeys.CostOfSales, t.CostOfSalesCents, 0, $"Cost of sales {day}");
        Add(AccountKeys.Stock, 0, t.CostOfSalesCents, $"Stock sold {day}");
        Add(AccountKeys.Stock, t.PurchasesCents, 0, $"Stock bought {day}");
        Add(AccountKeys.Creditors, 0, t.PurchasesCents, $"Owed to suppliers for stock {day}");

        if (missing is not null) return Result<Journal>.Fail("accounting.mapping_incomplete", $"Choose an account for '{missing}' before sending.");
        var journal = new Journal(t.Day, lines);
        return journal.Balances ? Result<Journal>.Ok(journal) : Result<Journal>.Fail("accounting.unbalanced", "The day's figures do not add up, so nothing was sent.");
    }
}

public enum ExportStatus { Pending, Succeeded, Failed }

/// <summary>One day's journal sent to one package. One row per shop, provider and day, so a day is never posted twice.</summary>
public sealed class ExportRun : VersionedEntity
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    private ExportRun() { }

    public Guid ShopId { get; private set; }
    public string Provider { get; private set; } = default!;
    public DateOnly Day { get; private set; }
    public ExportStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public string? ExternalReference { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public bool OwnerTold { get; private set; }

    public static ExportRun Start(Guid shopId, string provider, DateOnly day, DateTimeOffset now)
    {
        var r = new ExportRun { ShopId = shopId, Provider = provider, Day = day, Status = ExportStatus.Pending };
        r.StampFromDevice(Guid.CreateVersion7(), now, null, null);
        return r;
    }

    public bool ShouldTry(DateTimeOffset now) => Status != ExportStatus.Succeeded && Attempts < MaxAttempts && (LastAttemptAt is null || now - LastAttemptAt >= RetryAfter);

    public bool GivenUp => Status == ExportStatus.Failed && Attempts >= MaxAttempts;

    public void Succeeded(string reference, DateTimeOffset now)
    {
        Attempts++;
        Status = ExportStatus.Succeeded;
        ExternalReference = reference;
        LastError = null;
        LastAttemptAt = now;
        Touch();
    }

    public void Failed(string error, DateTimeOffset now)
    {
        Attempts++;
        Status = ExportStatus.Failed;
        LastError = error.Length > 300 ? error[..300] : error;
        LastAttemptAt = now;
        Touch();
    }

    /// <summary>The owner fixed something (mapping, connection) and asks to try again: the retry count starts over.</summary>
    public void Reopen()
    {
        if (Status == ExportStatus.Succeeded) return;
        Attempts = 0;
        Status = ExportStatus.Pending;
        LastAttemptAt = null;
        OwnerTold = false;
        Touch();
    }

    public void NoteOwnerTold()
    {
        OwnerTold = true;
        Touch();
    }
}
