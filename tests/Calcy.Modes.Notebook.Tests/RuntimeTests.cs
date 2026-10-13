using Calcy.Core;
using Calcy.Core.Printing;
using Calcy.Modes.Notebook.Model;
using Calcy.Modes.Notebook.Runtime;

namespace Calcy.Modes.Notebook.Tests;

/// <summary>The reactive document model: recalculation order, caching, errors, staleness and code cells.</summary>
public class RuntimeTests
{
    private static NotebookRuntime Create(params NotebookCell[] cells) => new(new NotebookDocument("Test", cells), new CalcSettings());

    private static string Value(MathLine line)
    {
        Assert.NotNull(line.Result);
        Assert.False(line.Result!.IsError, line.Result.Result.ErrorMessage);
        return ExprPrinter.ToInput(line.Result.Result.Exact!);
    }

    [Fact]
    public void ChangingADefinitionRecalculatesLaterCells()
    {
        var a = new MathCell(["a = 5"]);
        var b = new MathCell(["b = a + 1", "b*10"]);
        var text = new TextCell("Twice b is $=2b$.");
        using var rt = Create(a, b, text);
        rt.Recalculate();
        Assert.Equal("6", Value(b.Lines[0]));
        Assert.Equal("60", Value(b.Lines[1]));
        Assert.Equal("12", ExprPrinter.ToInput(text.LiveValues.Single().Value!));

        a.Lines[0].Text = "a = 6";
        Assert.True(rt.IsDirty);
        rt.Recalculate();
        Assert.Equal("7", Value(b.Lines[0]));
        Assert.Equal("70", Value(b.Lines[1]));
        Assert.Equal("14", ExprPrinter.ToInput(text.LiveValues.Single().Value!));
        Assert.All(new NotebookCell[] { a, b, text }, c => Assert.Equal(CellState.Ready, c.State));
    }

    [Fact]
    public void UnchangedLinesReuseTheirResults()
    {
        var a = new MathCell(["a = 5"]);
        var c = new MathCell(["c = 100", "d = c + a"]);
        using var rt = Create(a, c);
        rt.Recalculate();
        var cResult = c.Lines[0].Result;
        var dResult = c.Lines[1].Result;
        a.Lines[0].Text = "a = 7";
        rt.Recalculate();
        Assert.Same(cResult!.Result, c.Lines[0].Result!.Result);
        Assert.NotSame(dResult!.Result, c.Lines[1].Result!.Result);
        Assert.Equal("107", Value(c.Lines[1]));
    }

    [Fact]
    public void CellsSeeOnlyTheDefinitionsAboveThem()
    {
        var early = new MathCell(["y = k + 1"]);
        var def = new MathCell(["k = 3"]);
        var late = new MathCell(["z = k + 1"]);
        using var rt = Create(early, def, late);
        rt.Recalculate();
        Assert.Equal("k+1", Value(early.Lines[0]).Replace(" ", ""));
        Assert.Equal("4", Value(late.Lines[0]));

        // Moving the definition to the top makes the first cell use it.
        rt.Document.Move(def, 0);
        rt.Recalculate();
        Assert.Equal("4", Value(early.Lines[0]));
    }

    [Fact]
    public void FunctionDefinitionsAreUsedLater()
    {
        var f = new MathCell(["f(x) = x^2"]);
        var use = new MathCell(["f(3)"]);
        using var rt = Create(f, use);
        rt.Recalculate();
        Assert.Equal("9", Value(use.Lines[0]));
        f.Lines[0].Text = "f(x) = x^3";
        rt.Recalculate();
        Assert.Equal("27", Value(use.Lines[0]));
    }

    [Fact]
    public void ErrorsAreMarkedAndCleared()
    {
        var bad = new MathCell(["x = 1/0"]);
        var after = new MathCell(["2 +"]);
        using var rt = Create(bad, after);
        rt.Recalculate();
        Assert.Equal(CellState.Error, bad.State);
        Assert.True(bad.Lines[0].Result!.IsError);
        Assert.False(string.IsNullOrEmpty(bad.StateMessage));
        Assert.Equal(CellState.Error, after.State);

        bad.Lines[0].Text = "x = 2";
        after.Lines[0].Text = "2 + x";
        rt.Recalculate();
        Assert.Equal(CellState.Ready, bad.State);
        Assert.Null(bad.StateMessage);
        Assert.Equal("4", Value(after.Lines[0]));
        Assert.Equal(CellState.Ready, after.State);
    }

    [Fact]
    public void SliderLineRewritesTheDefinition()
    {
        var model = new MathCell(["L = 0.8", "g = 9.81", "T = 2*pi*sqrt(L/g)"]);
        using var rt = Create(model);
        rt.Recalculate();
        double t1 = Approx(model.Lines[2]);
        Assert.Equal(1.7943, t1, 3);
        Assert.True(model.Lines[0].CanSlide);
        model.Lines[0].SetRange(0.1, 2, 0.01);
        model.Lines[0].SetSliderValue(1.234);
        Assert.Equal("L = 1.23", model.Lines[0].Text);
        rt.Recalculate();
        Assert.Equal(2 * Math.PI * Math.Sqrt(1.23 / 9.81), Approx(model.Lines[2]), 6);
    }

    private static double Approx(MathLine line)
    {
        var r = line.Result!.Result;
        var e = r.Approx ?? r.Exact!;
        return double.Parse(ExprPrinter.ToInput(e), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void GraphSlidersDriveLaterCells()
    {
        var graph = new GraphCell(["y = a*x"]);
        var after = new MathCell(["b = 2a"]);
        using var rt = Create(graph, after);
        rt.Recalculate();
        Assert.NotNull(graph.Document);
        Assert.Contains(graph.Sliders, s => s.Name == "a");
        Assert.Equal("2", Value(after.Lines[0]));

        graph.SetSlider("a", 3);
        Assert.True(rt.IsDirty);
        rt.Recalculate();
        Assert.Equal("6", Value(after.Lines[0]));
    }

    [Fact]
    public void GraphSeesDefinitionsAboveAndDefinesFunctionsBelow()
    {
        var k = new MathCell(["k = 2"]);
        var graph = new GraphCell(["f(x) = k x", "y = f(x) + 1"]);
        var use = new MathCell(["f(5)"]);
        using var rt = Create(k, graph, use);
        rt.Recalculate();
        Assert.DoesNotContain(graph.Sliders, s => s.Name == "k");
        Assert.Equal("10", Value(use.Lines[0]));
        Assert.Equal(CellState.Ready, graph.State);
    }

    [Fact]
    public void TableRowsBecomeListsAndSummariesUseThem()
    {
        var table = new TableCell(3);
        table.AddRow("v", ["2", "4", "9"]);
        table.AddSummary("mean(v)");
        var after = new MathCell(["sum(v)"]);
        using var rt = Create(table, after);
        rt.Recalculate();
        Assert.Equal("5", Value(table.Summary[0]));
        Assert.Equal("15", Value(after.Lines[0]));
        table.SetValue(table.Rows[0], 2, "3");
        rt.Recalculate();
        Assert.Equal("3", Value(table.Summary[0]));

        table.SetName(table.Rows[0], "2x");
        rt.Recalculate();
        Assert.True(table.Rows[0].Result!.IsError);
        Assert.Equal(CellState.Error, table.State);
    }

    [Fact]
    public async Task CodeCellsShareVariables()
    {
        var first = new CodeCell("x = 21");
        var second = new CodeCell("print(x * 2)");
        using var rt = Create(first, second);
        rt.Recalculate();
        Assert.Equal(CellState.NotRun, first.State);
        var r1 = await rt.RunAsync(first);
        Assert.True(r1!.Success, r1.Output);
        var r2 = await rt.RunAsync(second);
        Assert.True(r2!.Success, r2.Output);
        Assert.Equal("42", second.Output.PlainText());
        Assert.Equal(CellState.Ready, second.State);
        Assert.StartsWith("ran in", second.RunSummary);
    }

    [Fact]
    public async Task CalcSetInCodeIsSeenByLaterMathsCells()
    {
        var code = new CodeCell("import calc\ncalc.set('k', 7)");
        var maths = new MathCell(["m = k * 2"]);
        using var rt = Create(code, maths);
        rt.Recalculate();
        // Before the code runs the line waits for it instead of showing a misleading value.
        Assert.Equal(CellState.Stale, maths.State);
        Assert.True(maths.Lines[0].Result!.Tainted);

        var r = await rt.RunAsync(code);
        Assert.True(r!.Success, r.Output);
        Assert.Equal("14", Value(maths.Lines[0]));
        Assert.Equal(CellState.Ready, maths.State);
    }

    [Fact]
    public async Task CodeSeesTheMathsAboveIt()
    {
        var maths = new MathCell(["r = 3"]);
        var code = new CodeCell("import calc\nprint(calc.get('r') ** 2)");
        var later = new MathCell(["r = 100"]);
        using var rt = Create(maths, code, later);
        rt.Recalculate();
        var result = await rt.RunAsync(code);
        Assert.True(result!.Success, result.Output);
        Assert.Equal("9", code.Output.PlainText());
    }

    [Fact]
    public async Task CodeCellBecomesStaleAndCanRunAgainByItself()
    {
        var model = new MathCell(["L = 0.8", "T = 2*pi*sqrt(L/9.81)"]);
        var code = new CodeCell("import calc\nT = calc.n('T')\nprint(f'{10*T:.1f}')");
        using var rt = Create(model, code);
        rt.Recalculate();
        await rt.RunAsync(code);
        Assert.Equal("17.9", code.Output.PlainText());
        Assert.Equal(CellState.Ready, code.State);

        model.Lines[0].Text = "L = 1.6";
        rt.Recalculate();
        Assert.Equal(CellState.Stale, code.State);
        Assert.Contains("T", code.StateMessage);
        Assert.Contains(code, rt.AutoRunCandidates);

        await rt.RunStaleAsync();
        Assert.Equal("25.4", code.Output.PlainText());
        Assert.Equal(CellState.Ready, code.State);

        // Edited code is not run by itself.
        code.Code += "\nprint('more')";
        rt.Recalculate();
        Assert.Equal(CellState.Stale, code.State);
        Assert.DoesNotContain(code, rt.AutoRunCandidates);
    }

    [Fact]
    public async Task ErrorsInCodeAreShownWithTheTraceback()
    {
        var code = new CodeCell("print('start')\n1/0");
        using var rt = Create(code);
        var r = await rt.RunAsync(code);
        Assert.False(r!.Success);
        Assert.Equal(CellState.Error, code.State);
        var items = code.Output.Items;
        Assert.Equal("start\n", items[0].Text);
        Assert.Contains(items, i => i.Kind == CodeOutputKind.Failure && i.Text.Contains("ZeroDivisionError"));
    }

    [Fact]
    public async Task InputIsAnsweredInline()
    {
        var code = new CodeCell("name = input('Name? ')\nprint('Hi ' + name)");
        using var rt = Create(code);
        rt.InputHandler = (_, prompt, _) => Task.FromResult<string?>(prompt == "Name? " ? "Ada" : "?");
        var r = await rt.RunAsync(code);
        Assert.True(r!.Success, r.Output);
        var input = code.Output.Items.Single(i => i.Kind == CodeOutputKind.Input);
        Assert.Equal("Name? ", input.Prompt);
        Assert.Equal("Ada", input.Answer);
        Assert.Contains("Hi Ada", code.Output.PlainText());
        Assert.DoesNotContain(code, rt.AutoRunCandidates);
    }

    [Fact]
    public async Task DrawingsAndGraphsAppearInTheOutput()
    {
        var code = new CodeCell("import calc, ti_draw\nti_draw.fill_rect(10, 10, 40, 30)\ncalc.graph.add('y = x^2')\ncalc.show('sqrt(8)')");
        using var rt = Create(code);
        var r = await rt.RunAsync(code);
        Assert.True(r!.Success, r.Output);
        var items = code.Output.Items;
        Assert.Contains(items, i => i.Kind == CodeOutputKind.Drawing && i.Frame is { HasDrawing: true });
        var graph = Assert.Single(items, i => i.Kind == CodeOutputKind.Graph);
        Assert.Equal("y = x^2", graph.Graphs.Single().Text);
        Assert.NotNull(graph.GraphSession);
        Assert.Contains(items, i => i.Kind == CodeOutputKind.Math);
    }

    [Fact]
    public async Task RunAllRunsEveryCodeCellInOrderWithFreshGlobals()
    {
        var a = new CodeCell("n = 1");
        var b = new CodeCell("n = n + 1\nprint(n)");
        using var rt = Create(a, b);
        await rt.RunAllAsync();
        Assert.Equal("2", b.Output.PlainText());
        await rt.RunAllAsync();
        Assert.Equal("2", b.Output.PlainText());
    }

    [Fact]
    public async Task ClearOutputsMarksCodeAsNotRun()
    {
        var code = new CodeCell("import calc\ncalc.set('q', 4)\nprint('done')");
        var maths = new MathCell(["q + 1"]);
        using var rt = Create(code, maths);
        await rt.RunAsync(code);
        Assert.Equal("5", Value(maths.Lines[0]));
        rt.ClearOutputs();
        rt.Recalculate();
        Assert.True(code.Output.IsEmpty);
        Assert.Equal(CellState.NotRun, code.State);
        Assert.Equal(CellState.Stale, maths.State);
    }

    [Fact]
    public void CodeNeverRunsWhenANotebookIsOpened()
    {
        var lib = NotebookLibrary.CreateDefault();
        var code = lib.Current.Cells.OfType<CodeCell>().Single();
        using (var first = new NotebookRuntime(lib.Current, new CalcSettings())) first.Recalculate();
        var reopened = NotebookLibrary.FromData(Roundtrip(lib.ToData()));
        using var rt = new NotebookRuntime(reopened.Current, new CalcSettings());
        rt.Recalculate();
        var again = reopened.Current.Cells.OfType<CodeCell>().Single();
        Assert.Equal(CellState.NotRun, again.State);
        Assert.True(again.Output.IsEmpty);
        Assert.Contains(again, rt.UnrunCodeCells);
        Assert.Equal(code.Code, again.Code);
    }

    [Fact]
    public void SampleNotebookCalculates()
    {
        var doc = SampleNotebooks.PendulumLab();
        using var rt = new NotebookRuntime(doc, new CalcSettings());
        rt.Recalculate();
        var model = doc.Cells.OfType<MathCell>().Single();
        Assert.Equal(1.7943, Approx(model.Lines[2]), 3);
        var table = doc.Cells.OfType<TableCell>().Single();
        Assert.Equal(1.802, Approx(table.Summary[0]), 6);
        Assert.Equal(0.43, Approx(table.Summary[1]), 2);
        var conclusion = doc.Cells.OfType<TextCell>().Last();
        Assert.Equal("0.4", ExprPrinter.ToInput(ResultForms.LiveDisplay(conclusion.LiveValues.Single())));
        Assert.Equal(CellState.Ready, doc.Cells.OfType<GraphCell>().Single().State);
        Assert.Equal(CellState.NotRun, doc.Cells.OfType<CodeCell>().Single().State);
        foreach (var c in doc.Cells.Where(c => c.Kind != CellKind.Code)) Assert.True(c.State == CellState.Ready, $"{c.Kind}: {c.StateMessage}");
    }

    [Fact]
    public async Task RecalculateAsyncAppliesResults()
    {
        var a = new MathCell(["a = 2"]);
        var b = new MathCell(["a^10"]);
        using var rt = Create(a, b);
        await rt.RecalculateAsync();
        Assert.Equal("1024", Value(b.Lines[0]));
        Assert.False(rt.IsBusy);
    }

    internal static NotebookStoreData Roundtrip(NotebookStoreData data)
    {
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var json = System.Text.Json.JsonSerializer.Serialize(data, options);
        return System.Text.Json.JsonSerializer.Deserialize<NotebookStoreData>(json, options)!;
    }
}
