using System.Globalization;
using System.Text;

namespace TillKnowIT.Reporting;

/// <summary>
/// A small PDF writer for tabular reports. It uses the built-in Helvetica fonts, so there are no font files, no
/// third-party library and no licence to manage. Text is Latin-1: characters outside it print as "?".
/// Wide tables switch to landscape, long tables continue on new pages with the header repeated.
/// </summary>
internal static class SimplePdf
{
    private const double Margin = 36;
    private const double FontSize = 9;
    private const double RowHeight = 14;
    private const double CharWidth = 4.9;   // average Helvetica 9pt glyph width, used to size columns

    public static byte[] Render(ReportTable table)
    {
        var landscape = table.Columns.Count > 5;
        var (pageW, pageH) = landscape ? (842.0, 595.0) : (595.0, 842.0);
        var usable = pageW - 2 * Margin;

        // Column widths follow the longest text in each column, scaled down to fit the page.
        var texts = table.Rows.Select(r => r.Select((v, i) => CellText.ForPrint(v, table.Columns[i].Kind)).ToArray()).ToList();
        var want = table.Columns.Select((c, i) => Math.Max(c.Header.Length, texts.Count == 0 ? 0 : texts.Max(t => t[i].Length)) * CharWidth + 10).ToArray();
        var scale = Math.Min(1.0, usable / want.Sum());
        var widths = want.Select(w => w * scale).ToArray();
        // When the table is narrower than the page, spread the spare room so it does not huddle left.
        if (scale >= 1.0) widths = widths.Select(w => w * usable / want.Sum()).ToArray();

        var rowsPerPage = (int)((pageH - 2 * Margin - 60) / RowHeight);
        var pages = new List<string>();
        var index = 0;
        do
        {
            var content = new StringBuilder();
            var y = pageH - Margin;
            if (pages.Count == 0)
            {
                Text(content, "F2", 14, Margin, y - 12, table.Title);
                y -= 30;
            }
            DrawRow(content, "F2", table.Columns.Select(c => c.Header).ToArray(), table.Columns.Select(c => c.Kind).ToArray(), widths, ref y, header: true);
            for (var n = 0; n < rowsPerPage - (pages.Count == 0 ? 2 : 0) && index < texts.Count; n++, index++)
                DrawRow(content, "F1", texts[index], table.Columns.Select(c => c.Kind).ToArray(), widths, ref y, header: false);

            if (index >= texts.Count)
                foreach (var note in table.Notes)
                {
                    y -= RowHeight;
                    Text(content, "F1", FontSize, Margin, y, note);
                }

            pages.Add(content.ToString());
        } while (index < texts.Count);

        for (var p = 0; p < pages.Count; p++)
        {
            var footer = new StringBuilder();
            Text(footer, "F1", 8, pageW / 2 - 20, Margin / 2, $"Page {p + 1} of {pages.Count}");
            pages[p] += footer;
        }

        return Assemble(pages, pageW, pageH);
    }

    private static void DrawRow(StringBuilder sb, string font, string[] cells, ColumnKind[] kinds, double[] widths, ref double y, bool header)
    {
        y -= RowHeight;
        var x = Margin;
        for (var i = 0; i < cells.Length; i++)
        {
            var fits = Math.Max(1, (int)((widths[i] - 8) / CharWidth));
            var text = cells[i].Length > fits ? cells[i][..Math.Max(0, fits - 1)] + "." : cells[i];
            var right = kinds[i] is ColumnKind.Money or ColumnKind.Integer;
            var tx = right ? x + widths[i] - 4 - text.Length * CharWidth : x + 2;
            Text(sb, font, FontSize, Math.Max(x + 1, tx), y, text);
            x += widths[i];
        }
        // Thin rule under the row.
        sb.Append(CultureInfo.InvariantCulture, $"0.5 w {Margin:0.##} {y - 3:0.##} m {x:0.##} {y - 3:0.##} l S\n");
        if (header) sb.Append(CultureInfo.InvariantCulture, $"1 w {Margin:0.##} {y - 3:0.##} m {x:0.##} {y - 3:0.##} l S\n");
    }

    private static void Text(StringBuilder sb, string font, double size, double x, double y, string text) =>
        sb.Append(CultureInfo.InvariantCulture, $"BT /{font} {size:0.##} Tf {x:0.##} {y:0.##} Td ({Escape(text)}) Tj ET\n");

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var c = ch > 255 || (ch < 32 && ch != '\t') ? '?' : ch;
            if (c is '\\' or '(' or ')') sb.Append('\\');
            sb.Append(c == '\t' ? ' ' : c);
        }
        return sb.ToString();
    }

    private static byte[] Assemble(List<string> pageContents, double pageW, double pageH)
    {
        var latin1 = Encoding.Latin1;
        using var stream = new MemoryStream();
        var offsets = new List<long>();

        void Write(string s) => stream.Write(latin1.GetBytes(s));
        void BeginObject() { offsets.Add(stream.Position); Write($"{offsets.Count} 0 obj\n"); }

        Write("%PDF-1.4\n");
        // Object ids: 1 catalog, 2 pages, 3 regular font, 4 bold font, then (page, content) pairs.
        var pageIds = Enumerable.Range(0, pageContents.Count).Select(i => 5 + i * 2).ToList();

        BeginObject(); Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        BeginObject(); Write($"<< /Type /Pages /Count {pageContents.Count} /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] >>\nendobj\n");
        BeginObject(); Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");
        BeginObject(); Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

        for (var i = 0; i < pageContents.Count; i++)
        {
            var contentId = pageIds[i] + 1;
            BeginObject();
            Write(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageW:0.##} {pageH:0.##}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>\nendobj\n"));
            var body = latin1.GetBytes(pageContents[i]);
            BeginObject();
            Write($"<< /Length {body.Length} >>\nstream\n");
            stream.Write(body);
            Write("\nendstream\nendobj\n");
        }

        var xref = stream.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return stream.ToArray();
    }
}
