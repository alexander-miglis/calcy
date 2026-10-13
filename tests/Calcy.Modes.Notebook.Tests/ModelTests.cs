using Calcy.Core;
using Calcy.Modes.Notebook.Model;
using Calcy.Modes.Notebook.Runtime;

namespace Calcy.Modes.Notebook.Tests;

/// <summary>Cells, undo, the notebook list, saving and exporting.</summary>
public class ModelTests
{
    [Fact]
    public void StructuralChangesCanBeUndone()
    {
        var a = new MathCell(["a = 1"]);
        var b = new TextCell("hello");
        var doc = new NotebookDocument("Doc", [a, b]);
        Assert.False(doc.CanUndo);

        doc.Remove(a);
        Assert.Equal([b], doc.Cells);
        Assert.Equal("delete maths cell", doc.UndoLabel);
        Assert.True(doc.Undo());
        Assert.Equal([a, b], doc.Cells);
        Assert.True(doc.Redo());
        Assert.Equal([b], doc.Cells);
        doc.Undo();

        Assert.True(doc.Move(a, 1));
        Assert.Equal([b, a], doc.Cells);
        doc.Undo();
        Assert.Equal([a, b], doc.Cells);

        var copy = doc.Duplicate(a);
        Assert.Equal(3, doc.Cells.Count);
        Assert.Same(copy, doc.Cells[1]);
        Assert.NotEqual(a.Id, copy.Id);
        Assert.Equal("a = 1", ((MathCell)copy).Lines[0].Text);
        doc.Undo();
        Assert.Equal(2, doc.Cells.Count);
    }

    [Fact]
    public void NotebookListSupportsNewRenameDuplicateDelete()
    {
        var lib = NotebookLibrary.CreateDefault();
        Assert.Equal("Pendulum lab", lib.Current.Name);
        var created = lib.Create();
        Assert.Same(created, lib.Current);
        Assert.Equal("Untitled notebook", created.Name);
        Assert.Equal("Untitled notebook 2", lib.Create().Name);

        lib.Rename(created, "Pendulum lab");
        Assert.Equal("Pendulum lab 2", created.Name);

        var copy = lib.Duplicate(lib.Notebooks[0]);
        Assert.Equal("Pendulum lab copy", copy.Name);
        Assert.Equal(lib.Notebooks[0].Cells.Count, copy.Cells.Count);
        Assert.Same(copy, lib.Current);

        foreach (var n in lib.Notebooks.ToList()) lib.Delete(n);
        Assert.Single(lib.Notebooks);
        Assert.Same(lib.Notebooks[0], lib.Current);
    }

    [Fact]
    public void SavedNotebooksRoundTrip()
    {
        var lib = NotebookLibrary.CreateDefault();
        var doc = lib.Current;
        doc.Cells[1].Collapsed = true;
        var graph = doc.Cells.OfType<GraphCell>().Single();
        graph.Height = 300;
        using (var rt = new NotebookRuntime(doc, new CalcSettings()))
        {
            rt.Recalculate();
            graph.AddExpression("y = 1");
            graph.SetSlider("nothing", 1);
        }
        var data = RuntimeTests.Roundtrip(lib.ToData());
        var back = NotebookLibrary.FromData(data);
        var d2 = back.Current;
        Assert.Equal(doc.Name, d2.Name);
        Assert.Equal(doc.Id, d2.Id);
        Assert.Equal(doc.Cells.Select(c => (c.Kind, c.Id, c.Caption, c.Collapsed)), d2.Cells.Select(c => (c.Kind, c.Id, c.Caption, c.Collapsed)));

        var model = (MathCell)d2.Cells[1];
        Assert.Equal(["L = 0.8", "g = 9.81", "T = 2*pi*sqrt(L/g)"], model.Lines.Select(l => l.Text));
        Assert.True(model.Lines[0].ShowSlider);
        Assert.Equal((0.1, 2.0, 0.01), (model.Lines[0].SliderMin, model.Lines[0].SliderMax, model.Lines[0].SliderStep));
        Assert.True(model.Lines[2].DecimalFirst);

        var table = d2.Cells.OfType<TableCell>().Single();
        Assert.Equal("Tm", table.Rows[0].Name);
        Assert.Equal(["1.81", "1.79", "1.80", "1.82", "1.79"], table.Rows[0].Values);
        Assert.Equal(["Tbar = mean(Tm)", "dev = abs(Tbar - T)/T*100"], table.Summary.Select(l => l.Text));

        var g2 = d2.Cells.OfType<GraphCell>().Single();
        Assert.Equal(["y = 2*pi*sqrt(x/g)", "(L, T)", "(0.8, Tbar)", "y = 1"], g2.Expressions);
        Assert.Equal(300, g2.Height);
        Assert.Equal(graph.Viewport, g2.Viewport);

        var text = d2.Cells.OfType<TextCell>().First();
        Assert.StartsWith("# Pendulum lab", text.Text);
        Assert.Equal(doc.Cells.OfType<CodeCell>().Single().Code, d2.Cells.OfType<CodeCell>().Single().Code);
    }

    [Fact]
    public async Task ShortCodeOutputIsSavedAndShownUntilTheNextRun()
    {
        var code = new CodeCell("print('hello')");
        var doc = new NotebookDocument("N", [code]);
        using (var rt = new NotebookRuntime(doc, new CalcSettings())) await rt.RunAsync(code);
        var back = NotebookDocument.FromData(doc.ToData());
        var c2 = (CodeCell)back.Cells[0];
        Assert.Equal("hello", c2.PreviousOutput);
        Assert.True(c2.Output.IsEmpty);
    }

    [Fact]
    public void MarkdownParsesTheSubset()
    {
        var md = MarkdownDocument.Parse("""
            # Title
            Some **bold** and *italic* text with `code`, $x^2$ and $=a+1$.
            See [Calcy](https://example.org) or https://example.com.

            - one
            - two
            1. first
            > note
            ```
            raw $x$
            ```
            ---
            """);
        Assert.Equal([MdBlockKind.Heading, MdBlockKind.Paragraph, MdBlockKind.Bullet, MdBlockKind.Bullet, MdBlockKind.Numbered, MdBlockKind.Quote, MdBlockKind.Code, MdBlockKind.Rule],
            md.Blocks.Select(b => b.Kind));
        Assert.Equal((1, "Title"), md.Headings.Single());
        var p = md.Blocks[1].Inlines;
        Assert.Contains(p, i => i is { Kind: MdInlineKind.Text, Bold: true, Text: "bold" });
        Assert.Contains(p, i => i is { Kind: MdInlineKind.Text, Italic: true, Text: "italic" });
        Assert.Contains(p, i => i is { Kind: MdInlineKind.Code, Text: "code" });
        Assert.Contains(p, i => i is { Kind: MdInlineKind.Math, Text: "x^2" });
        Assert.Contains(p, i => i is { Kind: MdInlineKind.Live, Text: "a+1", LiveIndex: 0 });
        Assert.Contains(p, i => i is { Kind: MdInlineKind.Link, Text: "Calcy", Url: "https://example.org" });
        Assert.Contains(p, i => i is { Kind: MdInlineKind.Link, Url: "https://example.com" });
        Assert.Equal(["a+1"], md.LiveSources);
        Assert.Equal("raw $x$", md.Blocks[6].Raw);
        Assert.Equal(1, md.Blocks[4].Number);
    }

    [Fact]
    public void MarkdownExportHasTextMathsTablesAndCode()
    {
        var doc = SampleNotebooks.PendulumLab();
        using var rt = new NotebookRuntime(doc, new CalcSettings());
        rt.Recalculate();
        var md = NotebookExporter.ToMarkdown(doc, rt.Session);
        Assert.StartsWith("# Pendulum lab\n", md);
        Assert.Contains("small-angle model $T = 2", md);
        Assert.Contains("**Model**", md);
        Assert.Contains("$$L = 0.8$$", md);
        Assert.Contains("\\sqrt", md);
        Assert.Contains("\\approx 1.794", md);
        Assert.Contains("| Tm | 1.81 | 1.79 | 1.80 | 1.82 | 1.79 |", md);
        Assert.Contains("```python\nimport calc\n", md);
        Assert.Contains("within $0.4$ %", md);
        Assert.DoesNotContain("\u2014", md);
        Assert.DoesNotContain("\u2013", md);

        var tex = NotebookExporter.ToLatex(doc, rt.Session);
        Assert.Contains("\\section*{Pendulum lab}", tex);
        Assert.Contains("\\begin{tabular}", tex);
        Assert.Contains("\\begin{verbatim}", tex);
        Assert.EndsWith("\\end{document}\n", tex);
    }

    [Fact]
    public async Task MarkdownExportIncludesCodeOutput()
    {
        var code = new CodeCell("print(6 * 7)");
        var doc = new NotebookDocument("Answers", [new MathCell(["x = 1/3"]), code]);
        using var rt = new NotebookRuntime(doc, new CalcSettings());
        rt.Recalculate();
        await rt.RunAsync(code);
        var md = NotebookExporter.ToMarkdown(doc, rt.Session);
        Assert.StartsWith("# Answers", md);
        Assert.Contains("$$x = \\frac{1}{3}", md);
        Assert.Contains("```text\n42\n```", md);
    }

    [Fact]
    public void SliderRangesAreSensible()
    {
        Assert.Equal((0.0, 2.0, 0.01), MathLine.DefaultRange(0.8));
        Assert.Equal((0.0, 20.0, 0.1), MathLine.DefaultRange(9.81));
        Assert.Equal((-10.0, 10.0, 0.05), MathLine.DefaultRange(-3));
        var line = new MathLine("a = 2");
        Assert.True(line.TryGetLiteral(out var name, out var v));
        Assert.Equal(("a", 2.0), (name, v));
        Assert.False(new MathLine("a = 2 + 1").CanSlide);
        Assert.False(new MathLine("f(x) = 2").CanSlide);
        line.SetSliderValue(3.333);
        Assert.Equal("a = 3.35", line.Text);
    }
}
