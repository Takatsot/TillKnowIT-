using System.Text;
using ClosedXML.Excel;
using TillKnowIT.Reporting;

namespace TillKnowIT.Reporting.Tests;

public class ExporterTests
{
    private static ReportTable Sample(params object?[][] rows) => new("Sales by day",
        [new ReportColumn("Day", ColumnKind.Date), new ReportColumn("Product"), new ReportColumn("Units", ColumnKind.Integer), new ReportColumn("Revenue", ColumnKind.Money)],
        rows);

    private static readonly object?[] Row1 = [new DateOnly(2026, 10, 26), "Bread, brown", 12, 21600L];

    // ---- CSV ----

    private static string Csv(ReportTable t) => Encoding.UTF8.GetString(ReportExporter.ToCsv(t)).TrimStart('﻿');

    [Fact]
    public void Csv_has_a_byte_order_mark_a_header_and_machine_friendly_numbers()
    {
        var bytes = ReportExporter.ToCsv(Sample(Row1));
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

        var lines = Csv(Sample(Row1)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Day,Product,Units,Revenue", lines[0]);
        Assert.Equal("2026-10-26,\"Bread, brown\",12,216.00", lines[1]);
    }

    [Fact]
    public void Csv_quotes_commas_quotes_and_line_breaks()
    {
        var csv = Csv(Sample([new DateOnly(2026, 10, 26), "He said \"hi\"\nthen left", 1, 100L]));
        Assert.Contains("\"He said \"\"hi\"\"\nthen left\"", csv);
    }

    [Fact]
    public void Csv_does_not_let_text_run_as_an_excel_formula_but_keeps_negative_numbers()
    {
        var csv = Csv(Sample(
            [new DateOnly(2026, 10, 26), "=HYPERLINK(\"http://x\")", 1, -500L],
            [new DateOnly(2026, 10, 26), "-cmd", 1, 100L],
            [new DateOnly(2026, 10, 26), "+1", 1, 100L]));

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(" =HYPERLINK", lines[1]);
        Assert.EndsWith(",-5.00", lines[1]);
        Assert.Contains(", -cmd", lines[2].Replace(",\" -cmd", ", -cmd"));
        Assert.Contains(" +1", lines[3]);
    }

    [Fact]
    public void Csv_money_uses_a_dot_whatever_the_machine_language()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("af-ZA");
            Assert.Contains(",1234.50", Csv(Sample([new DateOnly(2026, 10, 26), "x", 1, 123450L])));
        }
        finally { Thread.CurrentThread.CurrentCulture = old; }
    }

    // ---- XLSX ----

    [Fact]
    public void Xlsx_opens_with_typed_cells_and_keeps_text_as_text()
    {
        var table = Sample(Row1, [new DateOnly(2026, 10, 27), "=1+1", 3, 5050L]);
        using var workbook = new XLWorkbook(new MemoryStream(ReportExporter.ToXlsx(table)));
        var sheet = workbook.Worksheet(1);

        Assert.Equal("Day", sheet.Cell(1, 1).GetString());
        Assert.Equal(new DateTime(2026, 10, 26), sheet.Cell(2, 1).GetDateTime());
        Assert.Equal("Bread, brown", sheet.Cell(2, 2).GetString());
        Assert.Equal(12, sheet.Cell(2, 3).GetDouble());
        Assert.Equal(216.0, sheet.Cell(2, 4).GetDouble());
        Assert.Contains("R", sheet.Cell(2, 4).Style.NumberFormat.Format);
        Assert.Equal("=1+1", sheet.Cell(3, 2).GetString());
        Assert.False(sheet.Cell(3, 2).HasFormula);
    }

    [Fact]
    public void Xlsx_sheet_name_is_safe_and_notes_follow_the_table()
    {
        var table = new ReportTable("Stock: on/hand?", [new ReportColumn("Item")], [["Bread"]]) { Notes = ["Total items: 1"] };
        using var workbook = new XLWorkbook(new MemoryStream(ReportExporter.ToXlsx(table)));

        Assert.Equal("Stock onhand", workbook.Worksheet(1).Name);
        Assert.Equal("Total items: 1", workbook.Worksheet(1).Cell(4, 1).GetString());
    }

    // ---- PDF ----

    private static string PdfText(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public void Pdf_is_well_formed_and_its_cross_reference_table_points_at_the_objects()
    {
        var pdf = ReportExporter.Export(Sample(Row1), ReportFormat.Pdf, "sales").Content;
        var text = PdfText(pdf);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);

        var startXref = long.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", text[(int)startXref..]);

        // Every offset in the table lands on "<n> 0 obj".
        var entries = text[(int)startXref..].Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ")).ToList();
        for (var n = 0; n < entries.Count; n++)
            Assert.StartsWith($"{n + 1} 0 obj", text[int.Parse(entries[n][..10])..]);
    }

    [Fact]
    public void Pdf_shows_the_title_rows_and_money_with_a_rand_sign()
    {
        var text = PdfText(ReportExporter.Export(Sample(Row1), ReportFormat.Pdf, "sales").Content);
        Assert.Contains("(Sales by day)", text);
        Assert.Contains("(Bread, brown)", text);
        Assert.Contains("(R 216.00)", text);
    }

    [Fact]
    public void Long_reports_continue_on_new_pages_with_page_numbers()
    {
        var rows = Enumerable.Range(0, 200).Select(i => (object?[])[new DateOnly(2026, 10, 1).AddDays(i % 28), $"Item {i}", i, i * 100L]).ToArray();
        var text = PdfText(ReportExporter.Export(Sample(rows), ReportFormat.Pdf, "big").Content);

        var pages = System.Text.RegularExpressions.Regex.Matches(text, "/Type /Page ").Count;
        Assert.True(pages >= 4, $"expected several pages, got {pages}");
        Assert.Contains($"(Page 1 of {pages})", text);
        Assert.Contains($"(Page {pages} of {pages})", text);
        Assert.Contains("(Item 199)", text);
    }

    [Fact]
    public void Pdf_escapes_brackets_and_replaces_characters_it_cannot_print()
    {
        var text = PdfText(ReportExporter.Export(Sample([new DateOnly(2026, 10, 26), "Cola (2l) \\ 米", 1, 100L]), ReportFormat.Pdf, "x").Content);
        Assert.Contains(@"Cola \(2l\) \\ ?", text);
    }

    [Fact]
    public void Wide_tables_go_landscape_and_an_empty_report_still_renders()
    {
        var wide = new ReportTable("Wide", Enumerable.Range(0, 7).Select(i => new ReportColumn($"C{i}")).ToList(), [["a", "b", "c", "d", "e", "f", "g"]]);
        Assert.Contains("/MediaBox [0 0 842 595]", PdfText(ReportExporter.Export(wide, ReportFormat.Pdf, "w").Content));

        var empty = PdfText(ReportExporter.Export(Sample(), ReportFormat.Pdf, "e").Content);
        Assert.Contains("(Sales by day)", empty);
        Assert.Contains("(Page 1 of 1)", empty);
    }

    // ---- dispatch ----

    [Theory]
    [InlineData(null, true, ReportFormat.Csv)]
    [InlineData("csv", true, ReportFormat.Csv)]
    [InlineData("XLSX", true, ReportFormat.Xlsx)]
    [InlineData("pdf", true, ReportFormat.Pdf)]
    [InlineData("docx", false, ReportFormat.Csv)]
    public void Formats_parse_from_text(string? text, bool ok, ReportFormat expected)
    {
        Assert.Equal(ok, ReportFormats.TryParse(text, out var format));
        if (ok) Assert.Equal(expected, format);
    }

    [Fact]
    public void Export_names_the_file_and_sets_the_content_type()
    {
        var file = ReportExporter.Export(Sample(Row1), ReportFormat.Xlsx, "sales-2026-10");
        Assert.Equal("sales-2026-10.xlsx", file.FileName);
        Assert.Contains("spreadsheetml", file.ContentType);
    }
}
