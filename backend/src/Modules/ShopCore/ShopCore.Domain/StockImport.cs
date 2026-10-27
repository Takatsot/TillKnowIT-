using System.Text;
using TillKnowIT.SharedKernel;

namespace ShopCore.Domain;

public enum ImportStatus { Queued, Reading, Ready, Confirmed, Failed }

/// <summary>A sheet is read straight away. An invoice photo or PDF is queued (it may be taken offline) and read when online.</summary>
public enum ImportKind { Sheet, Invoice }

/// <summary>A delivery being brought into stock. Nothing is saved to stock until the owner confirms the reviewed lines.</summary>
public sealed class InvoiceImport : VersionedEntity
{
    private InvoiceImport() { }

    public InvoiceImport(Guid shopId, ImportKind kind, string fileUrl, string fileName, string? supplierName, Guid staffId, Guid deviceId, DateTimeOffset now)
    {
        ShopId = shopId;
        Kind = kind;
        FileUrl = fileUrl;
        FileName = fileName;
        SupplierName = string.IsNullOrWhiteSpace(supplierName) ? null : supplierName.Trim();
        Status = ImportStatus.Queued;
        StampFromDevice(Guid.CreateVersion7(), now, staffId, deviceId);
    }

    public Guid ShopId { get; private set; }
    public ImportKind Kind { get; private set; }
    public string FileUrl { get; private set; } = default!;
    public string FileName { get; private set; } = default!;
    public string? SupplierName { get; private set; }
    public ImportStatus Status { get; private set; }
    public string? ResultJson { get; private set; }
    public string? Error { get; private set; }

    public void StartReading()
    {
        Status = ImportStatus.Reading;
        Touch();
    }

    public void Complete(string planJson)
    {
        ResultJson = planJson;
        Error = null;
        Status = ImportStatus.Ready;
        Touch();
    }

    public void Fail(string error)
    {
        Error = error.Length > 500 ? error[..500] : error;
        Status = ImportStatus.Failed;
        Touch();
    }

    public Result Confirm()
    {
        if (Status == ImportStatus.Confirmed) return Result.Fail("import.already_confirmed", "This delivery has already been received.");
        if (Status != ImportStatus.Ready) return Result.Fail("import.not_ready", "This import is not ready to be received.");
        Status = ImportStatus.Confirmed;
        Touch();
        return Result.Ok();
    }
}

/// <summary>The column choices made for one supplier, kept so the next file from them needs no matching.</summary>
public sealed class ImportMapping : VersionedEntity
{
    private ImportMapping() { }

    public ImportMapping(Guid shopId, string supplierName, string mappingJson, DateTimeOffset now)
    {
        ShopId = shopId;
        SupplierKey = KeyFor(supplierName);
        SupplierName = supplierName.Trim();
        MappingJson = mappingJson;
        StampFromDevice(Guid.CreateVersion7(), now, null, null);
    }

    /// <summary>"Makro", " makro " and "MAKRO" are one supplier.</summary>
    public static string KeyFor(string supplierName) => string.Join(' ', supplierName.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public Guid ShopId { get; private set; }
    public string SupplierKey { get; private set; } = default!;
    public string SupplierName { get; private set; } = default!;
    public string MappingJson { get; private set; } = default!;

    public void Update(string mappingJson)
    {
        MappingJson = mappingJson;
        Touch();
    }
}

public sealed record ProductRef(Guid Id, string Name, string? Barcode, long CostCents);

/// <summary>One line as read from a sheet or an invoice, before it is matched to a product.</summary>
public sealed record RawImportLine(int Row, string Text, string? Barcode, int? Quantity, int? UnitsPerCase, long? CaseCostCents, long? UnitCostCents);

/// <summary>One line of the review table. Quantity is in units, cost is per unit: cases are already worked out.</summary>
public sealed record ImportLine(
    int Row, string Text, string? Barcode, Guid? ProductId, string MatchedBy, List<Guid> Candidates,
    int Quantity, int? UnitsPerCase, long? UnitCostCents, List<string> Issues);

public static class ImportPlanner
{
    public const double CandidateThreshold = 0.5;

    /// <summary>
    /// Turns raw lines into the review table: finds each product (barcode first, then exact name, otherwise suggestions),
    /// works out units from cases and cost per unit from the case price, and flags anything the owner must look at.
    /// </summary>
    public static List<ImportLine> Plan(IEnumerable<RawImportLine> raw, IReadOnlyList<ProductRef> products)
    {
        var byBarcode = products.Where(p => p.Barcode is not null).GroupBy(p => p.Barcode!).ToDictionary(g => g.Key, g => g.First());
        var byName = products.GroupBy(p => Normalise(p.Name)).ToDictionary(g => g.Key, g => g.ToList());

        return raw.Select(line =>
        {
            var issues = new List<string>();
            var barcode = string.IsNullOrWhiteSpace(line.Barcode) ? null : line.Barcode.Trim();
            var text = line.Text.Trim();
            if (barcode is null && text.Length == 0) issues.Add("name_or_barcode_missing");

            Guid? productId = null;
            var matchedBy = "none";
            var candidates = new List<Guid>();
            if (barcode is not null && byBarcode.TryGetValue(barcode, out var byCode))
            {
                (productId, matchedBy) = (byCode.Id, "barcode");
            }
            else if (text.Length > 0 && byName.TryGetValue(Normalise(text), out var sameName))
            {
                if (sameName.Count == 1) (productId, matchedBy) = (sameName[0].Id, "name");
                else { candidates = sameName.Select(p => p.Id).ToList(); issues.Add("ambiguous_name"); }
            }
            if (productId is null && candidates.Count == 0 && text.Length > 0)
                candidates = products.Select(p => (p.Id, Score: Overlap(text, p.Name))).Where(x => x.Score >= CandidateThreshold)
                    .OrderByDescending(x => x.Score).Take(3).Select(x => x.Id).ToList();
            if (productId is null && !issues.Contains("name_or_barcode_missing") && !issues.Contains("ambiguous_name")) issues.Add("no_match");

            // Cases become units; the case price becomes a price per unit.
            var perCase = line.UnitsPerCase is > 0 ? line.UnitsPerCase : null;
            int quantity = 0;
            if (line.Quantity is > 0) quantity = checked(line.Quantity.Value * (perCase ?? 1));
            else issues.Add("quantity_invalid");

            long? unitCost = line.UnitCostCents;
            if (unitCost is null && line.CaseCostCents is { } caseCost)
                unitCost = perCase is { } n ? (caseCost * 2 + n) / (n * 2) : caseCost;   // nearest cent, halves up
            if (unitCost is null) issues.Add("cost_missing");

            return new ImportLine(line.Row, text, barcode, productId, matchedBy, candidates, quantity, perCase, unitCost, issues);
        }).ToList();
    }

    /// <summary>What the owner must settle before confirming: lines with no product and lines with a problem the till cannot fix itself.</summary>
    public static bool NeedsAttention(ImportLine line) => line.ProductId is null || line.Issues.Contains("quantity_invalid");

    public static string Normalise(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Share of words in common, 0 to 1. "Coke 330ml can" and "coke can 330ml" score 1; "bread" and "brown bread" score 0.5.</summary>
    public static double Overlap(string a, string b)
    {
        var x = Normalise(a).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var y = Normalise(b).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        return x.Count == 0 || y.Count == 0 ? 0 : (double)x.Intersect(y).Count() / Math.Max(x.Count, y.Count);
    }
}

/// <summary>What the owner settled in the review table.</summary>
public sealed record ConfirmedLine(Guid ProductId, int Quantity, long? UnitCostCents);

public static class ImportConfirmation
{
    /// <summary>One receive movement per product, all sharing supplier and invoice number. Two lines for one product are added together.</summary>
    public static Result<List<StockMovementInput>> ToMovements(
        IEnumerable<ConfirmedLine> lines, IReadOnlySet<Guid> knownProducts, Guid shopId, Guid staffId, Guid deviceId,
        string? supplierName, string? invoiceRef, DateTimeOffset now)
    {
        var list = lines.ToList();
        if (list.Count == 0) return Result<List<StockMovementInput>>.Fail("import.no_lines", "Confirm at least one line.");

        var merged = new Dictionary<Guid, (int Quantity, long? Cost)>();
        foreach (var l in list)
        {
            if (!knownProducts.Contains(l.ProductId)) return Result<List<StockMovementInput>>.Fail("import.product_unknown", "A line points at a product this shop does not have.");
            if (l.Quantity <= 0) return Result<List<StockMovementInput>>.Fail("stock.quantity_sign", "A receive movement has the wrong sign.");
            if (l.UnitCostCents < 0) return Result<List<StockMovementInput>>.Fail("stock.cost_invalid", "Cost cannot be negative.");
            merged[l.ProductId] = merged.TryGetValue(l.ProductId, out var m) ? (m.Quantity + l.Quantity, l.UnitCostCents ?? m.Cost) : (l.Quantity, l.UnitCostCents);
        }

        var supplier = string.IsNullOrWhiteSpace(supplierName) ? null : supplierName.Trim();
        var invoice = string.IsNullOrWhiteSpace(invoiceRef) ? null : invoiceRef.Trim();
        return Result<List<StockMovementInput>>.Ok(merged.Select(kv => new StockMovementInput(
            Guid.CreateVersion7(), shopId, kv.Key, StockMovementType.Receive, kv.Value.Quantity, staffId, deviceId, now, null,
            kv.Value.Cost, null, supplier, invoice)).ToList());
    }
}
