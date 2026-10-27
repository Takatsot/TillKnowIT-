using System.Text;
using ClosedXML.Excel;

namespace TillKnowIT.Reporting;

public static class ReportExporter
{
    public static ReportFile Export(ReportTable table, ReportFormat format, string baseName) => format switch
    {
        ReportFormat.Csv => new ReportFile(ToCsv(table), "text/csv; charset=utf-8", baseName + ".csv"),
        ReportFormat.Xlsx => new ReportFile(ToXlsx(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", baseName + ".xlsx"),
        ReportFormat.Pdf => new ReportFile(SimplePdf.Render(table), "application/pdf", baseName + ".pdf"),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>UTF-8 with a byte-order mark, so Excel shows isiZulu, Sesotho and other accents correctly.</summary>
    internal static byte[] ToCsv(ReportTable table)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(',', table.Columns.Select(c => Quote(c.Header))).Append("\r\n");
        foreach (var row in table.Rows)
            sb.AppendJoin(',', row.Select((v, i) => Quote(CellText.ForCsv(v, table.Columns[i].Kind)))).Append("\r\n");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>
    /// Quotes a value when needed. Text that starts with = + @ (or - followed by a non-digit) would be run as a formula
    /// when the file is opened in Excel, so it is prefixed with a space to make it plain text. Negative numbers are left alone.
    /// </summary>
    private static string Quote(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '@' || (value.Length > 1 && value[0] == '-' && !char.IsDigit(value[1])))
            value = " " + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    internal static byte[] ToXlsx(ReportTable table)
    {
        using var workbook = new XLWorkbook();
        var name = new string(table.Title.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        var sheet = workbook.Worksheets.Add(name.Length == 0 ? "Report" : name[..Math.Min(name.Length, 31)]);

        for (var c = 0; c < table.Columns.Count; c++)
        {
            var header = sheet.Cell(1, c + 1);
            header.Value = table.Columns[c].Header;
            header.Style.Font.Bold = true;
        }

        for (var r = 0; r < table.Rows.Count; r++)
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                var value = table.Rows[r][c];
                switch (table.Columns[c].Kind)
                {
                    case ColumnKind.Money when value is not null:
                        cell.Value = Convert.ToInt64(value) / 100m;
                        cell.Style.NumberFormat.Format = "\"R\" #,##0.00";
                        break;
                    case ColumnKind.Integer when value is not null:
                        cell.Value = Convert.ToInt64(value);
                        break;
                    case ColumnKind.Date when value is DateOnly d:
                        cell.Value = d.ToDateTime(TimeOnly.MinValue);
                        cell.Style.DateFormat.Format = "yyyy-mm-dd";
                        break;
                    case ColumnKind.DateTime when value is DateTimeOffset t:
                        cell.Value = t.DateTime;
                        cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                        break;
                    default:
                        // Text is always stored as text, so Excel never runs it as a formula.
                        cell.SetValue(CellText.ForCsv(value, ColumnKind.Text));
                        break;
                }
            }

        var notesRow = table.Rows.Count + 3;
        foreach (var note in table.Notes) sheet.Cell(notesRow++, 1).SetValue(note);

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
