using System.Globalization;

namespace TillKnowIT.Reporting;

public enum ColumnKind { Text, Integer, Money, Date, DateTime }

public sealed record ReportColumn(string Header, ColumnKind Kind = ColumnKind.Text);

/// <summary>
/// A report as plain rows and typed columns, so one report definition can be written as CSV, Excel or PDF.
/// Cell values by kind: Text = string, Integer = int or long, Money = long cents, Date = DateOnly, DateTime = DateTimeOffset.
/// </summary>
public sealed record ReportTable(string Title, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<object?[]> Rows)
{
    /// <summary>Optional lines under the table, such as totals or the period covered.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public enum ReportFormat { Csv, Xlsx, Pdf }

public sealed record ReportFile(byte[] Content, string ContentType, string FileName);

public static class ReportFormats
{
    public static bool TryParse(string? value, out ReportFormat format)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "csv": format = ReportFormat.Csv; return true;
            case "xlsx": format = ReportFormat.Xlsx; return true;
            case "pdf": format = ReportFormat.Pdf; return true;
            default: format = default; return false;
        }
    }
}

/// <summary>How a cell is shown as text. The same rules feed CSV and PDF so they always agree.</summary>
public static class CellText
{
    /// <summary>Plain number for machines: money is rand with two decimals and a dot, whatever the computer's language.</summary>
    public static string ForCsv(object? value, ColumnKind kind) => kind switch
    {
        _ when value is null => "",
        ColumnKind.Money => Rand(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        ColumnKind.Integer => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        ColumnKind.Date => ((DateOnly)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ColumnKind.DateTime => ((DateTimeOffset)value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    public static string ForPrint(object? value, ColumnKind kind) =>
        kind == ColumnKind.Money && value is not null ? "R " + Rand(Convert.ToInt64(value, CultureInfo.InvariantCulture)) : ForCsv(value, kind);

    public static string Rand(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
