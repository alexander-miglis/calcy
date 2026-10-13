using Calcy.Modes.Statistics.Logic;
using static Calcy.Modes.Statistics.Tests.TestData;

namespace Calcy.Modes.Statistics.Tests;

/// <summary>Reading list cells and pasted tables.</summary>
public class CellAndPasteTests
{
    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("-2", -2)]
    [InlineData("+7", 7)]
    [InlineData(".25", 0.25)]
    [InlineData("3e-4", 3e-4)]
    [InlineData("1,5", 1.5)]
    [InlineData("1.234,5", 1234.5)]
    [InlineData("−4", -4)]
    [InlineData(" 12 ", 12)]
    public void Plain_numbers_and_decimal_commas(string text, double expected)
    {
        var v = CellParser.Parse(text);
        Assert.Equal(CellKind.Number, v.Kind);
        Assert.Equal(expected, v.Number, 12);
        Assert.NotNull(v.Exact);
    }

    [Fact]
    public void Expressions_need_the_session_and_words_are_text()
    {
        var s = Session();
        Assert.Equal(1.0 / 3, CellParser.Parse("1/3", s).Number, 12);
        Assert.Equal(Math.Sqrt(2), CellParser.Parse("sqrt(2)", s).Number, 12);
        Assert.Equal(Math.PI / 4, CellParser.Parse("pi/4", s).Number, 12);
        Assert.Equal(CellKind.Text, CellParser.Parse("1/3").Kind);
        Assert.Equal(CellKind.Text, CellParser.Parse("red", s).Kind);
        Assert.Equal("red", CellParser.Parse(" red ", s).Text);
        Assert.Equal(CellKind.Text, CellParser.Parse("1..5", s).Kind);
        Assert.Equal(CellKind.Empty, CellParser.Parse("   ", s).Kind);
        Assert.Equal("5", CellParser.Parse("\"5\"", s).Text);
        Assert.Equal(CellKind.Text, CellParser.Parse("\"5\"", s).Kind);
    }

    [Fact]
    public void Tab_separated_paste_with_a_header_row()
    {
        var p = TablePaste.Parse("hours\tscore\r\n1\t52\r\n2\t55\r\n2\t60\r\n")!;
        Assert.Equal(2, p.ColumnCount);
        Assert.Equal(3, p.RowCount);
        Assert.Equal(["hours", "score"], p.Header);
        Assert.Equal(["1", "2", "2"], p.Column(0));
        Assert.Equal(["52", "55", "60"], p.Column(1));
    }

    [Fact]
    public void Tab_separated_paste_with_decimal_commas()
    {
        var p = TablePaste.Parse("1,5\t2,25\n3\t1.234,5\n")!;
        Assert.Null(p.Header);
        Assert.Equal(["1.5", "3"], p.Column(0));
        Assert.Equal(["2.25", "1234.5"], p.Column(1));
    }

    [Fact]
    public void Tabs_with_decimal_points_keep_commas_as_thousands()
    {
        var p = TablePaste.Parse("1,234.5\t2.5\n3\t4.75\n")!;
        Assert.Equal(["1234.5", "3"], p.Column(0));
        Assert.Equal(["2.5", "4.75"], p.Column(1));
    }

    [Fact]
    public void Csv_with_commas_and_quoted_cells()
    {
        var p = TablePaste.Parse("x,y,label\n1,2.5,\"red, dark\"\n3,4,blue\n")!;
        Assert.Equal(3, p.ColumnCount);
        Assert.Equal(["x", "y", "label"], p.Header);
        Assert.Equal(["2.5", "4"], p.Column(1));
        Assert.Equal(["red, dark", "blue"], p.Column(2));
    }

    [Fact]
    public void Semicolon_csv_uses_decimal_commas()
    {
        var p = TablePaste.Parse("1,5;2\n3,25;4\n")!;
        Assert.Equal(["1.5", "3.25"], p.Column(0));
        Assert.Equal(["2", "4"], p.Column(1));
    }

    [Fact]
    public void One_column_with_decimal_commas_is_not_split()
    {
        var p = TablePaste.Parse("1,5\n2,25\n3\n")!;
        Assert.Equal(1, p.ColumnCount);
        Assert.Equal(["1.5", "2.25", "3"], p.Column(0));
    }

    [Fact]
    public void Spaces_lists_and_ragged_rows()
    {
        var spaces = TablePaste.Parse("1 2 3\n4 5 6")!;
        Assert.Equal(3, spaces.ColumnCount);
        Assert.Equal(["3", "6"], spaces.Column(2));

        var list = TablePaste.Parse("{4, 8, 15, 16, 23, 42}")!;
        Assert.Equal(1, list.ColumnCount);
        Assert.Equal(6, list.RowCount);
        Assert.Equal("42", list.Column(0)[5]);

        var ragged = TablePaste.Parse("1\t2\n3\n5\t6\t\n")!;
        Assert.Equal(2, ragged.ColumnCount);
        Assert.Equal(["2", "", "6"], ragged.Column(1));
        Assert.Null(TablePaste.Parse("  \n "));
        Assert.True(TablePaste.IsTable("1\t2"));
        Assert.False(TablePaste.IsTable("12"));
    }

    [Fact]
    public void Paste_into_the_table_adds_lists_and_captions()
    {
        var t = new StatTable(Session());
        t.AddColumn();
        var filled = t.Paste(TablePaste.Parse("height\tweight\n1,70\t65\n1,82\t80")!, 0, 0);
        Assert.Equal(["L1", "L2"], filled);
        Assert.Equal("height", t.Find("L1")!.Caption);
        Assert.Equal(["1.70", "1.82"], t.Find("L1")!.Cells);
        Assert.Equal(1.82, t.Find("L1")!.Value(1, t.Session).Number, 12);
        Assert.Equal(2, t.RowCount);
        // Pasting lower down keeps what is above.
        t.Paste(TablePaste.Parse("90")!, 1, 2);
        Assert.Equal(["65", "80", "90"], t.Find("L2")!.Cells);
    }
}
