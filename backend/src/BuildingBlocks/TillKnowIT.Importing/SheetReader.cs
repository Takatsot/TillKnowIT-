using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using TillKnowIT.SharedKernel;

namespace TillKnowIT.Importing;

/// <summary>A spreadsheet or CSV as plain text: a header row and the rows below it. Every cell is a trimmed string.</summary>
public sealed record Sheet(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

public static class SheetReader
{
    public const int MaxRows = 5000;
    public const int MaxColumns = 50;
    public const int MaxBytes = 5 * 1024 * 1024;

    public static Result<Sheet> Read(string fileName, byte[] content)
    {
        if (content.Length == 0) return Result<Sheet>.Fail("import.empty", "The file is empty.");
        if (content.Length > MaxBytes) return Result<Sheet>.Fail("import.too_large", "The file is larger than 5 MB.");

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            return extension switch
            {
                ".csv" or ".txt" => FromCsv(content),
                ".xlsx" => FromXlsx(content),
                _ => Result<Sheet>.Fail("import.type_invalid", "Upload an Excel (.xlsx) or CSV file."),
            };
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or NotSupportedException or FormatException)
        {
            return Result<Sheet>.Fail("import.unreadable", "That file could not be read. Is it a real Excel or CSV file?");
        }
    }

    // ---- CSV ----

    private static Result<Sheet> FromCsv(byte[] content)
    {
        var text = DecodeText(content);
        var delimiter = DetectDelimiter(text);
        var records = ParseCsv(text, delimiter);
        return Build(records);
    }

    /// <summary>UTF-8 (with or without a byte-order mark), falling back to Windows-1252 for files saved by older Excel.</summary>
    private static string DecodeText(byte[] bytes)
    {
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, start, bytes.Length - start); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes, start, bytes.Length - start); }
    }

    /// <summary>South African Excel saves with semicolons because the comma is the decimal mark. Pick whichever appears most in the header line.</summary>
    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        return new[] { ',', ';', '\t' }.OrderByDescending(d => firstLine.Count(c => c == d)).First();
    }

    internal static List<List<string>> ParseCsv(string text, char delimiter)
    {
        var records = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        void EndCell() { row.Add(cell.ToString().Trim()); cell.Clear(); }
        void EndRow() { EndCell(); if (row.Any(c => c.Length > 0)) records.Add(row); row = []; }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == delimiter) EndCell();
            else if (c == '\n') EndRow();
            else if (c != '\r') cell.Append(c);
            if (records.Count > MaxRows + 1) break;
        }
        if (cell.Length > 0 || row.Count > 0) EndRow();
        return records;
    }

    // ---- XLSX ----

    private static Result<Sheet> FromXlsx(byte[] content)
    {
        using var workbook = new XLWorkbook(new MemoryStream(content));
        var sheet = workbook.Worksheets.FirstOrDefault();
        var used = sheet?.RangeUsed();
        if (sheet is null || used is null) return Result<Sheet>.Fail("import.empty", "The workbook has no data.");

        var records = new List<List<string>>();
        foreach (var row in used.Rows())
        {
            if (records.Count > MaxRows + 1) break;
            records.Add(row.Cells().Select(CellText).Take(MaxColumns + 1).ToList());
        }
        return Build(records.Where(r => r.Any(c => c.Length > 0)).ToList());
    }

    // Numbers are read as numbers, not as the text Excel shows, so "1 234,50" and "R12.00" formats cannot trip us up.
    private static string CellText(IXLCell cell) => cell.DataType switch
    {
        XLDataType.Number => cell.GetDouble().ToString("0.############", CultureInfo.InvariantCulture),
        XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        XLDataType.Boolean => cell.GetBoolean() ? "true" : "false",
        _ => cell.GetString().Trim(),
    };

    private static Result<Sheet> Build(List<List<string>> records)
    {
        if (records.Count == 0) return Result<Sheet>.Fail("import.empty", "The file has no rows.");
        var headers = records[0];
        if (headers.Count > MaxColumns) return Result<Sheet>.Fail("import.too_wide", $"At most {MaxColumns} columns.");
        if (records.Count - 1 > MaxRows) return Result<Sheet>.Fail("import.too_many_rows", $"At most {MaxRows} rows. Split the file.");

        // Ragged rows are padded so every row has a value for every column.
        var rows = records.Skip(1).Select(r => (IReadOnlyList<string>)Enumerable.Range(0, headers.Count).Select(i => i < r.Count ? r[i] : "").ToList()).ToList();
        return Result<Sheet>.Ok(new Sheet(headers, rows));
    }
}
