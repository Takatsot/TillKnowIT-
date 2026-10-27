using System.Text;
using ClosedXML.Excel;
using TillKnowIT.Importing;

namespace TillKnowIT.Importing.Tests;

public class ImportingTests
{
    private static Sheet Csv(string text, bool bom = false, Encoding? encoding = null)
    {
        var bytes = (encoding ?? Encoding.UTF8).GetBytes(text);
        if (bom) bytes = [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. bytes];
        return SheetReader.Read("stock.csv", bytes).Value!;
    }

    // ---- CSV ----

    [Fact]
    public void A_plain_csv_reads_into_headers_and_rows()
    {
        var sheet = Csv("Barcode,Description,Qty\n6001234567890,Bread,12\n6009999999999,Milk,6\n");
        Assert.Equal(["Barcode", "Description", "Qty"], sheet.Headers);
        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal(["6001234567890", "Bread", "12"], sheet.Rows[0]);
    }

    [Theory]
    [InlineData("A;B;C\n1;2;3\n")]
    [InlineData("A\tB\tC\n1\t2\t3\n")]
    [InlineData("A,B,C\r\n1,2,3\r\n")]
    public void The_delimiter_is_detected_so_South_African_Excel_files_just_work(string text) =>
        Assert.Equal(["1", "2", "3"], Csv(text).Rows[0]);

    [Fact]
    public void Quoted_cells_keep_commas_quotes_and_line_breaks_and_a_byte_order_mark_is_ignored()
    {
        var sheet = Csv("Name,Note\n\"Bread, brown\",\"He said \"\"hi\"\"\nthen left\"\n", bom: true);
        Assert.Equal("Name", sheet.Headers[0]);
        Assert.Equal("Bread, brown", sheet.Rows[0][0]);
        Assert.Equal("He said \"hi\"\nthen left", sheet.Rows[0][1]);
    }

    [Fact]
    public void Files_saved_by_older_Excel_in_Windows_1252_keep_their_accents()
    {
        var sheet = SheetReader.Read("old.csv", Encoding.Latin1.GetBytes("Name;Qty\nCafé crème;3\n")).Value!;
        Assert.Equal("Café crème", sheet.Rows[0][0]);
    }

    [Fact]
    public void Blank_lines_are_skipped_short_rows_are_padded_and_cells_are_trimmed()
    {
        var sheet = Csv("A,B,C\n\n  1 , 2\n\n3,4,5\n");
        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal(["1", "2", ""], sheet.Rows[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",,\n,,\n")]
    public void Empty_files_are_refused(string text) =>
        Assert.Equal("import.empty", SheetReader.Read("a.csv", Encoding.UTF8.GetBytes(text)).Error!.Code);

    [Fact]
    public void Blank_rows_at_the_top_are_skipped_so_the_first_real_row_is_the_header()
    {
        var sheet = Csv(",,\nName,Qty\nBread,3\n");
        Assert.Equal(["Name", "Qty"], sheet.Headers);
    }

    [Fact]
    public void Limits_stop_huge_files_and_unknown_types()
    {
        var many = "A\n" + string.Join("\n", Enumerable.Repeat("x", SheetReader.MaxRows + 1));
        Assert.Equal("import.too_many_rows", SheetReader.Read("a.csv", Encoding.UTF8.GetBytes(many)).Error!.Code);
        Assert.Equal("import.too_large", SheetReader.Read("a.csv", new byte[SheetReader.MaxBytes + 1]).Error!.Code);
        Assert.Equal("import.type_invalid", SheetReader.Read("a.exe", [1, 2, 3]).Error!.Code);
        Assert.Equal("import.too_wide", SheetReader.Read("a.csv", Encoding.UTF8.GetBytes(string.Join(",", Enumerable.Range(0, 60).Select(i => "c" + i)) + "\n1\n")).Error!.Code);
    }

    // ---- XLSX ----

    private static byte[] Xlsx(Action<IXLWorksheet> fill)
    {
        using var workbook = new XLWorkbook();
        fill(workbook.Worksheets.Add("Stock"));
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public void An_excel_file_reads_numbers_as_numbers_and_text_as_text()
    {
        var bytes = Xlsx(ws =>
        {
            ws.Cell(1, 1).Value = "Barcode"; ws.Cell(1, 2).Value = "Cost"; ws.Cell(1, 3).Value = "Qty";
            ws.Cell(2, 1).SetValue("6001234567890");              // a barcode stored as text keeps its digits
            ws.Cell(2, 2).Value = 12.5; ws.Cell(2, 2).Style.NumberFormat.Format = "\"R\" #,##0.00";
            ws.Cell(2, 3).Value = 24;
        });

        var sheet = SheetReader.Read("stock.xlsx", bytes).Value!;

        Assert.Equal(["Barcode", "Cost", "Qty"], sheet.Headers);
        Assert.Equal(["6001234567890", "12.5", "24"], sheet.Rows[0]);
    }

    [Fact]
    public void A_corrupt_workbook_or_an_empty_one_gets_a_clear_message_not_a_crash()
    {
        Assert.Equal("import.unreadable", SheetReader.Read("a.xlsx", Encoding.UTF8.GetBytes("this is not a zip")).Error!.Code);
        Assert.Equal("import.empty", SheetReader.Read("a.xlsx", Xlsx(_ => { })).Error!.Code);
    }

    // ---- column matching ----

    [Fact]
    public void Common_header_names_are_matched_to_the_right_fields()
    {
        var map = ColumnMapper.Suggest(["EAN", "Item Description", "Qty Ordered", "Case Size", "Case Price", "Retail Price"]).FieldToHeader;

        Assert.Equal("EAN", map["barcode"]);
        Assert.Equal("Item Description", map["name"]);
        Assert.Equal("Qty Ordered", map["quantity"]);
        Assert.Equal("Case Size", map["units_per_case"]);
        Assert.Equal("Case Price", map["case_cost"]);
        Assert.Equal("Retail Price", map["price"]);
    }

    [Fact]
    public void A_column_is_never_used_for_two_fields_and_unknown_headers_are_left_for_the_owner()
    {
        var map = ColumnMapper.Suggest(["Weird", "Product", "Product Code"]).FieldToHeader;

        Assert.Equal("Product", map["name"]);
        Assert.Equal("Product Code", map["barcode"]);
        Assert.DoesNotContain("Weird", map.Values);
        Assert.Equal(map.Values.Count, map.Values.Distinct().Count());
    }

    [Fact]
    public void A_saved_mapping_is_reused_only_while_its_columns_are_still_in_the_file()
    {
        var saved = new ColumnMapping(new() { ["name"] = "Stuff", ["quantity"] = "How many" });

        Assert.Same(saved, ColumnMapper.Reconcile(saved, ["stuff", "HOW MANY", "Other"]));     // header case does not matter
        var changed = ColumnMapper.Reconcile(saved, ["Barcode", "Description", "Qty"]);          // the supplier changed their file
        Assert.NotSame(saved, changed);
        Assert.Equal("Description", changed.FieldToHeader["name"]);
        Assert.Equal("Barcode", ColumnMapper.Reconcile(null, ["Barcode"]).FieldToHeader["barcode"]);
    }

    [Fact]
    public void A_cell_is_found_by_its_mapped_header_whatever_the_case()
    {
        var sheet = new Sheet(["Description", "Qty"], [["Bread", "12"]]);
        var mapping = new ColumnMapping(new() { ["name"] = "description", ["quantity"] = "QTY" });

        Assert.Equal("Bread", ColumnMapper.Cell(sheet, sheet.Rows[0], mapping, "name"));
        Assert.Equal("12", ColumnMapper.Cell(sheet, sheet.Rows[0], mapping, "quantity"));
        Assert.Null(ColumnMapper.Cell(sheet, sheet.Rows[0], mapping, "barcode"));
    }

    // ---- numbers ----

    [Theory]
    [InlineData("18", 1800)]
    [InlineData("18.5", 1850)]
    [InlineData("18,50", 1850)]
    [InlineData("R 1 234,50", 123450)]
    [InlineData("R1,234.50", 123450)]
    [InlineData("1 234", 123400)]
    [InlineData("1,234", 123400)]
    [InlineData("0", 0)]
    [InlineData("ZAR 5", 500)]
    public void Money_is_read_the_South_African_way(string text, long cents) => Assert.Equal(cents, ColumnMapper.ParseMoneyCents(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("18.555")]
    [InlineData(null)]
    public void Things_that_are_not_money_are_null(string? text) => Assert.Null(ColumnMapper.ParseMoneyCents(text));

    [Theory]
    [InlineData("12", 12)]
    [InlineData("12.0", 12)]
    [InlineData(" 24 ", 24)]
    public void Whole_numbers_accept_a_trailing_zero_from_excel(string text, int expected) => Assert.Equal(expected, ColumnMapper.ParseWholeNumber(text));

    [Theory]
    [InlineData("1.5")]
    [InlineData("-3")]
    [InlineData("many")]
    [InlineData("")]
    public void Anything_else_is_not_a_whole_number(string text) => Assert.Null(ColumnMapper.ParseWholeNumber(text));
}
