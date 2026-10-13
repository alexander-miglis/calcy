using Calcy.Core.Expressions;
using Calcy.Core.Printing;
using Calcy.Modes.Statistics.Logic;
using Calcy.Statistics;
using static Calcy.Modes.Statistics.Tests.TestData;

namespace Calcy.Modes.Statistics.Tests;

/// <summary>List storage in the session, column formulas, sorting, renaming and frequency lists.</summary>
public class TableAndFormulaTests
{
    [Fact]
    public void Lists_are_session_variables_with_exact_values()
    {
        var s = Session();
        var t = DesignTable(s);
        Assert.Equal("{1, 2, 2, 3, 4, 5, 5, 6, 7, 8}", ExprPrinter.ToInput(s.Env.Variables["L1"]));
        t.SetCell("L1", 0, "0.1");
        Assert.Equal("1/10", ExprPrinter.ToInput(((ListExpr)s.Env.Variables["L1"]).Items[0]));
        var mean = s.Evaluate("mean(L1)");
        Assert.Equal("421/100", ExprPrinter.ToInput(mean.Exact!));
        t.Clear("L1");
        Assert.False(s.Env.Variables.ContainsKey("L1"));
    }

    [Fact]
    public void Column_formula_squares_a_list_and_stays_live()
    {
        var s = Session();
        var t = DesignTable(s);
        Assert.Null(t.ApplyFormula("L3 = L1^2"));
        var l3 = t.Find("L3")!;
        Assert.Equal("L1^2", l3.Formula);
        Assert.Equal(["1", "4", "4", "9", "16", "25", "25", "36", "49", "64"], l3.Cells);
        t.SetCell("L1", 9, "10");
        Assert.Equal("100", l3[9]);
        Assert.Equal("{1, 4, 4, 9, 16, 25, 25, 36, 49, 100}", ExprPrinter.ToInput(s.Env.Variables["L3"]));
    }

    [Fact]
    public void Formula_with_division_keeps_exact_values_in_the_session()
    {
        var s = Session();
        var t = DesignTable(s);
        Assert.Null(t.ApplyFormula("L3 = L2 / L1"));
        Assert.Equal("27.5", t.Find("L3")![1]);
        Assert.Equal("55/2", ExprPrinter.ToInput(((ListExpr)s.Env.Variables["L3"]).Items[1]));
        // 61/3 has no finite decimal: the cell keeps the exact spelling, which reads back exactly.
        Assert.Equal("61/3", t.Find("L3")![3]);
        Assert.Equal(61.0 / 3, t.Find("L3")!.Value(3, s).Number, 12);
    }

    [Fact]
    public void Seq_formulas_formula_chains_and_errors()
    {
        var s = Session();
        var t = new StatTable(s);
        t.AddColumn("L1");
        Assert.Null(t.ApplyFormula("L1 = seq(k^2, k, 1, 5)"));
        Assert.Equal(["1", "4", "9", "16", "25"], t.Find("L1")!.Cells);
        Assert.Null(t.ApplyFormula("L2 = L1 + 1"));
        Assert.Null(t.ApplyFormula("L3 = 2*L2"));
        Assert.Equal("52", t.Find("L3")![4]);
        Assert.Null(t.ApplyFormula("L1 = seq(k, k, 1, 3)"));
        Assert.Equal(["4", "6", "8"], t.Find("L3")!.Cells);
        Assert.Contains("itself", t.ApplyFormula("L4 = L4 + 1"));
        Assert.Contains("one number", t.ApplyFormula("L5 = 3 + 4"));
        Assert.NotNull(t.Find("L5")!.FormulaError);
        Assert.False(ColumnFormula.TryParse("L1 <= 3", out _, out _));
        Assert.True(ColumnFormula.TryParse("L3 = \"L1^2\"", out var target, out var rhs));
        Assert.Equal(("L3", "L1^2"), (target, rhs));
        // Text without "name =" goes to the list that is given.
        Assert.Null(t.ApplyFormula("L1*10", "L6"));
        Assert.Equal(["10", "20", "30"], t.Find("L6")!.Cells);
    }

    [Fact]
    public void Typing_into_a_formula_list_turns_it_into_data()
    {
        var t = DesignTable();
        t.ApplyFormula("L3 = L1 + 100");
        t.SetCell("L3", 0, "5");
        var l3 = t.Find("L3")!;
        Assert.Null(l3.Formula);
        Assert.Equal("5", l3[0]);
        Assert.Equal("102", l3[1]);
        t.SetCell("L1", 1, "50");
        Assert.Equal("102", l3[1]);
    }

    [Fact]
    public void Rename_moves_the_variable_and_updates_formulas()
    {
        var s = Session();
        var t = DesignTable(s);
        t.ApplyFormula("L3 = L1 * 2");
        Assert.Null(t.Rename("L1", "hours", "per week"));
        Assert.True(s.Env.Variables.ContainsKey("hours"));
        Assert.False(s.Env.Variables.ContainsKey("L1"));
        Assert.Equal("hours * 2", t.Find("L3")!.Formula);
        Assert.Equal("hours per week", t.Find("hours")!.Header);
        Assert.Equal("16", t.Find("L3")![9]);
        Assert.NotNull(t.Rename("hours", "sin"));
        Assert.NotNull(t.Rename("hours", "L2"));
        Assert.NotNull(t.Rename("hours", "2x"));
        Assert.NotNull(t.Rename("hours", "x"));
        Assert.Equal("L1", t.NextName());
    }

    [Fact]
    public void Sort_reorders_rows_so_pairs_stay_together()
    {
        var t = new StatTable(Session());
        t.AddColumn("L1");
        t.AddColumn("L2");
        t.SetCells("L1", ["3", "1", "", "2", "red"]);
        t.SetCells("L2", ["30", "10", "99", "20", "x"]);
        t.SortRows("L1");
        Assert.Equal(["1", "2", "3", "red"], t.Find("L1")!.Cells);
        Assert.Equal(["10", "20", "30", "x", "99"], t.Find("L2")!.Cells);
        t.SortRows("L2", descending: true);
        Assert.Equal(["99", "30", "20", "10", "x"], t.Find("L2")!.Cells);
        Assert.Equal(["", "3", "2", "1", "red"], t.Find("L1")!.Cells);
    }

    [Fact]
    public void Lists_defined_elsewhere_come_in_and_deleted_ones_empty_the_column()
    {
        var s = Session();
        var t = DesignTable(s);
        s.Evaluate("L4 = {7, 8, 9}");
        s.Evaluate("L2 = {1, 1/4}");
        s.Evaluate("notalist = 5");
        var changed = t.PullFromSession();
        Assert.Contains("L4", changed);
        Assert.Contains("L2", changed);
        Assert.Equal(["L1", "L2", "L4"], t.Columns.Select(c => c.Name));
        Assert.Equal(["1", "0.25"], t.Find("L2")!.Cells);
        s.Env.Remove("L4");
        t.PullFromSession();
        Assert.True(t.Find("L4")!.IsEmpty);
        Assert.Empty(t.PullFromSession());
    }

    [Fact]
    public void Gaps_are_undefined_in_the_session_and_skipped_in_analyses()
    {
        var s = Session();
        var t = new StatTable(s);
        t.AddColumn("L1");
        t.SetCells("L1", ["4", "", "6"]);
        Assert.Equal("{4, undefined, 6}", ExprPrinter.ToInput(s.Env.Variables["L1"]));
        var sample = DataSet.From(t).Single("L1");
        Assert.Equal([4.0, 6.0], sample.Values);
        Assert.Equal(1, sample.Skipped);
    }

    [Fact]
    public void Frequency_lists_weight_the_values()
    {
        var data = DataSet.Of(("L1", [1, 2, 3]), ("L2", [2, 0, 3]));
        var withFreq = Descriptives.OneVar(data, new OneVarSettings { List = "L1", Freq = "L2" });
        var expanded = Descriptives.OneVar(DataSet.Of(("L1", [1, 1, 3, 3, 3])), new OneVarSettings { List = "L1" });
        Assert.Null(withFreq.Error);
        Assert.Equal(5, withFreq.Stats!.N);
        Assert.Equal(expanded.Stats!.Mean, withFreq.Stats.Mean, 12);
        Assert.Equal(expanded.Stats.SampleStdDev, withFreq.Stats.SampleStdDev, 12);
        Assert.Equal(expanded.Stats.Median, withFreq.Stats.Median, 12);
        Assert.Equal("onevar(L1, L2)", withFreq.Card.UseText);

        var bad = Descriptives.OneVar(DataSet.Of(("L1", [1, 2]), ("L2", [1, -1])), new OneVarSettings { List = "L1", Freq = "L2" });
        Assert.Contains("negative", bad.Error);
        var t = new StatTable(Session());
        t.AddColumn("L1");
        t.AddColumn("L2");
        t.SetCells("L1", ["1", "2", "3"]);
        t.SetCells("L2", ["1", "", "1"]);
        var missing = Descriptives.OneVar(DataSet.From(t), new OneVarSettings { List = "L1", Freq = "L2" });
        Assert.Contains("frequency L2 row 2 is empty", missing.Error);
        t.SetCells("L2", ["1", "many", "1"]);
        Assert.Contains("text", Descriptives.OneVar(DataSet.From(t), new OneVarSettings { List = "L1", Freq = "L2" }).Error);
    }

    [Fact]
    public void One_var_tiles_card_and_use_text()
    {
        var o = Descriptives.OneVar(DesignData(), new OneVarSettings { List = "L2" });
        Assert.Null(o.Error);
        Assert.Equal("10", o.Tiles.First(t => t.Label == "n").Value);
        Assert.Equal("67.5", o.Tiles.First(t => t.Label == "mean x̄").Value);
        Assert.Equal("10.5856", o.Tiles.First(t => t.Label == "Sx").Value);
        Assert.Equal("1-VAR L2", o.Card.Title);
        Assert.Equal("onevar(L2)", o.Card.UseText);
        Assert.Contains("\\bar{x} &= 67.5", o.Card.Latex);
        Assert.Equal(StatTab.OneVar, o.Card.Recipe!.Tab);
        EvaluateUse(o.Card.UseText!);
        var text = Descriptives.OneVar(DataSet.From(TextTable()), new OneVarSettings { List = "L1" });
        Assert.Contains("L1 row 2 holds text ('oops')", text.Error);
    }

    private static StatTable TextTable()
    {
        var t = new StatTable(Session());
        t.AddColumn("L1");
        t.SetCells("L1", ["1", "oops", "3"]);
        return t;
    }

    [Fact]
    public void Quartile_method_changes_the_quartiles()
    {
        var data = DataSet.Of(("L1", [1, 2, 3, 4, 5, 6, 7]));
        var ti = Descriptives.OneVar(data, new OneVarSettings { List = "L1", Method = QuartileMethod.TI });
        var tukey = Descriptives.OneVar(data, new OneVarSettings { List = "L1", Method = QuartileMethod.TukeyHinges });
        Assert.Equal(2, ti.Stats!.Q1);
        Assert.Equal(2.5, tukey.Stats!.Q1);
    }
}
