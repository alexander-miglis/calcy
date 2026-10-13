using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Calcy.Core.Printing;
using Calcy.Modes.Notebook.Model;
using Calcy.Modes.Notebook.Views;
using Calcy.UI.Services;
using Calcy.UI.TestKit;

namespace Calcy.Modes.Notebook.Tests;

/// <summary>The Notebook screen: cells, keyboard, payloads, saving.</summary>
public class ScreenTests
{
    [Fact]
    public void ModeMetadata()
    {
        var m = new NotebookMode();
        Assert.Equal("notebook", m.Id);
        Assert.Equal("Notebook", m.Title);
        Assert.Equal("Text, maths and graphs in one document", m.Description);
        Assert.Equal("Ctrl+Shift+N", m.Shortcut);
        Assert.Equal("G4", m.AccentToken);
        Assert.Equal("notebook", m.Icon);
    }

    [Fact]
    public void StartsWithTheSampleNotebook()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            Assert.IsAssignableFrom<IBusy>(view);
            Assert.IsAssignableFrom<IFocusFirst>(view);
            Assert.IsAssignableFrom<IReceivesPayload>(view);
            Assert.Equal("Pendulum lab", view.Document.Name);
            Assert.Equal([CellKind.Text, CellKind.Maths, CellKind.Table, CellKind.Graph, CellKind.Code, CellKind.Text], view.CellViews.Select(v => v.Cell.Kind));
            var maths = (MathCellView)view.CellViews[1];
            Assert.Equal("Maths cell: Model", maths.HeaderText);
            Assert.True(maths.LineViews[2].ResultChip.IsVisible);
            Assert.True(maths.LineViews[0].Slider.IsEffectivelyVisible);
            Assert.False(maths.LineViews[0].ResultChip.IsVisible);
            var code = (CodeCellView)view.CellViews[4];
            Assert.Equal(CellState.NotRun, code.Cell.State);
            Assert.Equal(CellState.Ready, view.CellViews[3].Cell.State);
        });
    }

    [Fact]
    public void MovingTheSliderUpdatesEveryLaterResult()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            TestSupport.Await(view.RunAllAsync());
            TestSupport.WaitIdle(view);
            var code = (CodeCell)view.CellViews[4].Cell;
            Assert.Equal("10 swings take 17.9 s", code.Output.PlainText());

            var maths = (MathCellView)view.CellViews[1];
            maths.LineViews[0].Slider.Value = 1.6;
            maths.LineViews[0].Line.SetSliderValue(1.6);
            Assert.Equal("L = 1.6", maths.LineViews[0].Editor.Text);
            TestSupport.WaitIdle(view);
            var t = maths.LineViews[2].Line.Result!.Result;
            Assert.Equal(2 * Math.PI * Math.Sqrt(1.6 / 9.81), double.Parse(ExprPrinter.ToInput(t.Approx!), System.Globalization.CultureInfo.InvariantCulture), 6);
            // The code cell read T, so it runs again by itself.
            Assert.True(TestSupport.WaitUntil(() => code.Output.PlainText() == "10 swings take 25.4 s"));
            Assert.Equal(CellState.Ready, code.State);
        });
    }

    [Fact]
    public void ShiftEnterRunsTheCodeCell()
    {
        Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create();
            var code = (CodeCellView)view.CellViews[4];
            view.Select(code, focus: false);
            code.Focus();
            w.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null);
            Assert.True(TestSupport.WaitUntil(() => ((CodeCell)code.Cell).LastRun is not null && !view.IsBusy));
            Assert.Equal("10 swings take 17.9 s", ((CodeCell)code.Cell).Output.PlainText());
            Assert.Same(view.CellViews[5], view.SelectedView);
        });
    }

    [Fact]
    public void AltArrowsMoveTheSelectedCell()
    {
        Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create();
            var graph = view.CellViews[3];
            view.Select(graph, focus: false);
            graph.Focus();
            w.KeyPress(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, null);
            TestSupport.WaitIdle(view);
            Assert.Same(graph.Cell, view.Document.Cells[2]);
            w.KeyPress(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, null);
            w.KeyPress(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, null);
            TestSupport.WaitIdle(view);
            Assert.Same(graph.Cell, view.Document.Cells[4]);
        });
    }

    [Fact]
    public void DeletedCellsComeBackWithUndo()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            var table = view.CellViews[2].Cell;
            view.DeleteCell(table);
            TestSupport.WaitIdle(view);
            Assert.DoesNotContain(table, view.Document.Cells);
            Assert.True(view.Undo());
            TestSupport.WaitIdle(view);
            Assert.Same(table, view.Document.Cells[2]);
            Assert.Equal(CellState.Ready, table.State);
        });
    }

    [Fact]
    public void InsertingCellsAboveAndBelow()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            var anchor = view.Document.Cells[1];
            var above = view.InsertCell(CellKind.Text, anchor, below: false);
            var below = view.InsertCell(CellKind.Code, anchor, below: true);
            TestSupport.WaitIdle(view);
            Assert.Same(above, view.Document.Cells[1]);
            Assert.Same(anchor, view.Document.Cells[2]);
            Assert.Same(below, view.Document.Cells[3]);
            Assert.Same(below, view.SelectedCell);
            view.DuplicateCell(anchor);
            Assert.Equal(CellKind.Maths, view.Document.Cells[3].Kind);
        });
    }

    [Fact]
    public void PayloadsAppendCells()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            view.Receive(new InputRequest("Tbar*10", Evaluate: true));
            TestSupport.WaitIdle(view);
            var maths = Assert.IsType<MathCell>(view.Document.Cells[^1]);
            Assert.Equal("Tbar*10", maths.Lines[0].Text);
            Assert.Equal("901/50", ExprPrinter.ToInput(maths.Lines[0].Result!.Result.Exact!));

            view.Receive(new PlotRequest(["y = sin(x)", "y = cos(x)"]));
            TestSupport.WaitIdle(view);
            var graph = Assert.IsType<GraphCell>(view.Document.Cells[^1]);
            Assert.Equal(["y = sin(x)", "y = cos(x)"], graph.Expressions);
            Assert.Same(graph, view.SelectedCell);
        });
    }

    [Fact]
    public void FocusFirstFocusesTheFirstCell()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            view.FocusFirst();
            Headless.Pump();
            Assert.Same(view.CellViews[0], view.SelectedView);
            Assert.True(view.CellViews[0].IsKeyboardFocusWithin);
        });
    }

    [Fact]
    public void NotebooksAreSavedAndRestoredWithoutRunningCode()
    {
        Headless.Run(() =>
        {
            var (_, view, services) = TestSupport.Create();
            TestSupport.Await(view.RunAllAsync());
            TestSupport.WaitIdle(view);
            ((MathCell)view.Document.Cells[1]).Lines[1].Text = "g = 9.8";
            view.Library.Create("Second");
            TestSupport.WaitIdle(view);
            view.SaveNow();

            var again = (NotebookView)new NotebookMode().CreateView(services);
            Headless.Show(again, 1360, 812);
            Assert.True(TestSupport.WaitIdle(again));
            Assert.Equal(["Pendulum lab", "Second"], again.Library.Notebooks.Select(n => n.Name));
            Assert.Equal("Second", again.Document.Name);
            again.Library.Select(again.Library.Notebooks[0]);
            TestSupport.WaitIdle(again);
            Assert.Equal("g = 9.8", ((MathCell)again.Document.Cells[1]).Lines[1].Text);
            var code = (CodeCell)again.Document.Cells[4];
            Assert.Equal(CellState.NotRun, code.State);
            Assert.True(code.Output.IsEmpty);
            Assert.Equal("10 swings take 17.9 s", code.PreviousOutput);
        });
    }

    [Fact]
    public void CodeInputIsAnsweredInTheCell()
    {
        Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create();
            var cell = (CodeCell)view.InsertCell(CellKind.Code, null);
            cell.Code = "n = int(input('How many? '))\nprint(n * 2)";
            TestSupport.WaitIdle(view);
            var codeView = (CodeCellView)view.CellViews.Single(v => v.Cell == cell);
            var task = view.RunCodeAsync(cell);
            Assert.True(TestSupport.WaitUntil(() => codeView.PendingInput is { IsEffectivelyVisible: true }));
            codeView.PendingInput!.Focus();
            w.KeyTextInput("21");
            w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            TestSupport.Await(task);
            TestSupport.WaitIdle(view);
            Assert.Contains("42", cell.Output.PlainText());
            Assert.Equal(CellState.Ready, cell.State);
        });
    }

    [Fact]
    public void TextCellsEditAndPreview()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            var textView = (TextCellView)view.CellViews[5];
            textView.StartEditing();
            Headless.Pump();
            Assert.True(textView.IsEditing);
            textView.SourceEditor.Text = "## Result\n\nThe period is $=round(T, 3)$ s and **bold** stays bold.";
            textView.StopEditing();
            TestSupport.WaitIdle(view);
            Assert.False(textView.IsEditing);
            var live = ((TextCell)textView.Cell).LiveValues.Single();
            Assert.Equal("1.794", ExprPrinter.ToInput(ResultForms.LiveDisplay(live)));
            Assert.Single(textView.Preview.Headings);
        });
    }

    [Fact]
    public void MarkdownCanBeCopied()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            var md = TestSupport.Await(view.CopyMarkdownAsync());
            Assert.StartsWith("# Pendulum lab", md);
            Assert.Contains("```python", md);
        });
    }

    [Fact]
    public void NotebookListSwitchesNotebooks()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            var created = view.Library.Create();
            TestSupport.WaitIdle(view);
            Assert.Same(created, view.Document);
            Assert.Equal([CellKind.Text, CellKind.Maths], view.CellViews.Select(v => v.Cell.Kind));
            view.Library.Select(view.Library.Notebooks[0]);
            TestSupport.WaitIdle(view);
            Assert.Equal("Pendulum lab", view.Document.Name);
            Assert.Equal(6, view.CellViews.Count);
        });
    }
}
