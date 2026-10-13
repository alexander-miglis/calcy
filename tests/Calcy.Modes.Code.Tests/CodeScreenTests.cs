using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Calcy.Modes.Code.Programs;
using Calcy.Modes.Code.Views;
using Calcy.Python;
using Calcy.Scripting;
using Calcy.UI.Math;
using Calcy.UI.Services;
using Calcy.UI.TestKit;

namespace Calcy.Modes.Code.Tests;

public class CodeScreenTests
{
    [Fact]
    public void ModeMetadata()
    {
        var m = new CodeMode();
        Assert.Equal("code", m.Id);
        Assert.Equal("Code", m.Title);
        Assert.Equal("Sandboxed Python", m.Description);
        Assert.Equal("Ctrl+E", m.Shortcut);
        Assert.Equal("G1", m.AccentToken);
        Assert.Equal("code", m.Icon);
    }

    [Fact]
    public void StartsWithTheDesignPrograms()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            Assert.Equal(["primes.py", "star.py", "roots.py"], view.Store.Programs.Select(p => p.Name));
            Assert.Equal("primes.py", view.CurrentProgram.Name);
            Assert.StartsWith("# Primes below 50", view.Editor.Text);
            Assert.IsAssignableFrom<IBusy>(view);
            Assert.IsAssignableFrom<IFocusFirst>(view);
            Assert.IsAssignableFrom<IReceivesPayload>(view);
        });
    }

    [Fact]
    public void RunShowsOutputInARunCard()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            var r = TestSupport.Run(view);
            Assert.True(r!.Success, r.Output);
            var card = view.Cards.First();
            Assert.Equal("#1  primes.py", card.Title);
            Assert.Contains("15 primes: [2, 3, 5", card.ConsoleText);
            Assert.EndsWith("steps", card.Meta);
            Assert.False(card.IsError);
            Assert.NotNull(card.RerunButton);
            Assert.Equal("Finished primes.py", view.StatusText);
        });
    }

    [Fact]
    public void ProgramAsksForInputInline()
    {
        Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create();
            TestSupport.OpenSource(view, "hello.py", "name = input('Name? ')\nprint('Hi', name)");
            var task = view.RunCurrentAsync();
            Assert.True(TestSupport.WaitUntil(() => view.Cards.First().PendingInput is not null));
            var box = view.Cards.First().PendingInput!;
            Assert.True(box.IsFocused || TestSupport.WaitUntil(() => box.IsFocused, 2000));
            w.KeyTextInput("Ada");
            w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(TestSupport.WaitUntil(() => task.IsCompleted));
            var text = view.Cards.First().ConsoleText;
            Assert.Contains("Name? Ada", text);
            Assert.Contains("Hi Ada", text);
            Assert.True(task.Result!.Success);
        });
    }

    [Fact]
    public void TracebackLinesJumpToTheEditor()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            TestSupport.OpenSource(view, "oops.py", "def f(x):\n    return 10 / x\n\nprint(f(2))\nprint(f(0))");
            var r = TestSupport.Run(view);
            Assert.Equal(ExecutionStatus.Error, r!.Status);
            Assert.Equal(2, view.Editor.ErrorLine);
            Assert.Contains("ZeroDivisionError", view.Editor.ErrorMessage);
            var card = view.Cards.First();
            Assert.True(card.IsError);
            var links = card.LineLinks.ToList();
            Assert.Equal(2, links.Count);
            view.Editor.SetCaret(new Calcy.Modes.Code.Editor.TextPos(0, 0));
            links[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(4, view.Editor.Caret.Line);
            links[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, view.Editor.Caret.Line);
            Assert.Equal("Error on line 2", view.StatusText);
        });
    }

    [Fact]
    public void SyntaxErrorsAreMarkedInTheEditor()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            TestSupport.OpenSource(view, "typo.py", "x = 1\nif x > 0\n    print(x)");
            var r = TestSupport.Run(view);
            Assert.True(r!.Error!.IsSyntaxError);
            Assert.Equal(2, view.Editor.ErrorLine);
            Assert.Contains("SyntaxError", view.Editor.ErrorMessage);
            Assert.Single(view.Cards.First().LineLinks);
        });
    }

    [Fact]
    public void HeavyPrintingKeepsTheEndOfTheOutput()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            TestSupport.OpenSource(view, "count.py", "for i in range(60000):\n    print('line', i)");
            var sw = Stopwatch.StartNew();
            var r = TestSupport.Run(view, 60000);
            Assert.True(r!.Success);
            var text = view.Cards.First().ConsoleText;
            Assert.Contains("line 59999", text);
            Assert.DoesNotContain("line 10\n", text);
            Assert.Contains("Earlier output is hidden", text);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), sw.Elapsed.ToString());
        });
    }

    [Fact]
    public void StopEndsAnInfiniteLoopWithinASecond()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            TestSupport.OpenSource(view, "loop.py", "n = 0\nwhile True:\n    n += 1");
            var task = view.RunCurrentAsync();
            Assert.True(TestSupport.WaitUntil(() => view.Host.IsRunning));
            Assert.True(view.IsBusy);
            Thread.Sleep(200);
            var sw = Stopwatch.StartNew();
            view.Stop();
            Assert.True(TestSupport.WaitUntil(() => task.IsCompleted, 5000));
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), sw.Elapsed.ToString());
            Assert.Equal(ExecutionStatus.Cancelled, task.Result!.Status);
            var card = view.Cards.First();
            Assert.Equal("stopped by you", card.Meta);
            Assert.Contains("KeyboardInterrupt: stopped with the Stop button.", card.ConsoleText);
            Assert.False(view.IsRunning);
        });
    }

    [Fact]
    public void EscapeStopsTheProgram()
    {
        Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create();
            TestSupport.OpenSource(view, "loop.py", "while True:\n    pass");
            view.Editor.FocusEditor();
            var task = view.RunCurrentAsync();
            Assert.True(TestSupport.WaitUntil(() => view.Host.IsRunning));
            w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.True(TestSupport.WaitUntil(() => task.IsCompleted, 3000));
            Assert.Equal(ExecutionStatus.Cancelled, task.Result!.Status);
        });
    }

    [Fact]
    public void F5RunsTheProgram()
    {
        Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create();
            view.Editor.FocusEditor();
            w.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
            Assert.True(TestSupport.WaitUntil(() => view.Cards.Any() && !view.IsRunning && !view.Cards.First().IsRunning));
            Assert.Contains("15 primes", view.Cards.First().ConsoleText);
        });
    }

    [Fact]
    public void ReplUsesTheProgramsGlobalsAndTypesetsCalcResults()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            TestSupport.Run(view);
            var t1 = view.SubmitReplAsync("len(found)");
            Assert.True(TestSupport.WaitUntil(() => t1.IsCompleted));
            Assert.Equal("15", t1.Result!.ValueRepr);
            var t2 = view.SubmitReplAsync("import calc\ncalc.eval('sqrt(8)')");
            Assert.True(TestSupport.WaitUntil(() => t2.IsCompleted));
            var card = view.Cards.First();
            Assert.True(card.IsRepl);
            Assert.Contains(">>> len(found)", card.ConsoleText);
            Assert.Contains("15", card.ConsoleText);
            Assert.Contains("[math 2*sqrt(2)]", card.ConsoleText);
            Assert.NotEmpty(card.GetLogicalDescendantsOfType<MathView>());
            Assert.Equal(2, view.Cards.Count());
        });
    }

    [Fact]
    public void ReplHistoryAndContinuationLines()
    {
        Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create();
            var box = view.ReplBox;
            box.Focus();
            w.KeyTextInput("for i in range(3):");
            w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal("for i in range(3):\n    ", box.Text);
            w.KeyTextInput("print(i)");
            w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(TestSupport.WaitUntil(() => view.Cards.Any() && !view.IsBusy));
            Assert.Contains("0\n1\n2", view.Cards.First().ConsoleText);
            w.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.StartsWith("for i in range(3):", box.Text);
        });
    }

    [Fact]
    public void TurtleDrawsOnTheCanvasAndGraphsGoToGraphing()
    {
        Headless.Run(() =>
        {
            var (_, view, s) = TestSupport.Create();
            view.Open("star.py");
            TestSupport.Run(view);
            Assert.True(view.CanvasView.HasDrawing);
            Assert.Equal(SidePanel.Canvas, view.SidePanelShown);

            view.OpenExample(Examples.Find("wave.py")!);
            var r = TestSupport.Run(view);
            Assert.True(r!.Success, r.Output);
            Assert.Equal(SidePanel.Graph, view.SidePanelShown);
            Assert.Equal(["y = wave(x)", "y = exp(-x/6)"], view.GraphTexts);
            Assert.Equal(2, view.GraphCanvas.Items!.Count);
            NavigationRequest? nav = null;
            s.NavigationRequested += (_, n) => nav = n;
            view.SendToGraphing();
            Assert.Equal("graphing", nav!.ModeId);
            Assert.Equal(["y = wave(x)", "y = exp(-x/6)"], ((PlotRequest)nav.Payload!).Expressions);
            // The Python function stays usable in calculator expressions.
            Assert.False(s.Session.Evaluate("wave(0)").IsError);
        });
    }

    [Fact]
    public void KeysOnTheCanvasReachTheProgram()
    {
        Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create();
            TestSupport.OpenSource(view, "keys.py", """
import turtle
s = turtle.Screen()
t = turtle.Turtle()
t.speed(0)
def up():
    t.forward(10)
def quit():
    raise SystemExit
s.onkey(up, "Up")
s.onkey(quit, "q")
s.listen()
s.mainloop()
""");
            var task = view.RunCurrentAsync();
            Assert.True(TestSupport.WaitUntil(() => view.Host.IsRunning && view.CanvasView.IsFocused, 5000));
            w.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            w.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.True(TestSupport.WaitUntil(() => view.Host.Canvas.Commands.Any(c => c.Kind == Calcy.Scripting.Drawing.DrawKind.Line), 5000));
            w.KeyPress(Key.Q, RawInputModifiers.None, PhysicalKey.Q, "q");
            Assert.True(TestSupport.WaitUntil(() => task.IsCompleted, 5000));
            Assert.Equal(ExecutionStatus.Exited, task.Result!.Status);
            Assert.True(task.Result.Success);
        });
    }

    [Fact]
    public void VariablesInspectorListsGlobals()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            TestSupport.OpenSource(view, "vars.py", "count = 3\nname = 'Ada'\nitems = [1, 2]");
            TestSupport.Run(view);
            view.ShowSide(SidePanel.Variables);
            Calcy.UI.TestKit.Headless.Pump();
            var texts = view.GetLogicalDescendantsOfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("count", texts);
            Assert.Contains("'Ada'", texts);
            Assert.Contains("[1, 2]", texts);
        });
    }

    [Fact]
    public void ProgramsAreSavedAndRestored()
    {
        Headless.Run(() =>
        {
            var (_, view, s) = TestSupport.Create();
            var p = view.NewProgram();
            Assert.Equal("program.py", p.Name);
            view.Editor.InsertText("print('saved')");
            Assert.True(view.Rename(p, "keep me"));
            TestSupport.Run(view);
            var r = view.SubmitReplAsync("1 + 1");
            Assert.True(TestSupport.WaitUntil(() => r.IsCompleted));
            view.SaveNow();
            Assert.True(s.Prefs.ModeState.ContainsKey("code"));

            var again = (CodeView)new CodeMode().CreateView(s);
            Assert.Equal("keep me.py", again.CurrentProgram.Name);
            Assert.Equal("print('saved')", again.Editor.Text);
            Assert.Contains(again.Store.Programs, x => x.Name == "primes.py");
            Assert.Equal(2, again.Cards.Count());
            Assert.Contains("saved", again.Cards.Last().ConsoleText);
            again.ReplBox.Focus();
        });
    }

    [Fact]
    public void FileOperations()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            var primes = view.Store.Find("primes.py")!;
            var copy = view.Duplicate(primes);
            Assert.Equal("primes 2.py", copy.Name);
            Assert.Equal(primes.Source, copy.Source);
            Assert.Equal(copy, view.CurrentProgram);
            Assert.False(view.Rename(copy, "  /  "));
            Assert.True(view.Rename(copy, "star"));
            Assert.Equal("star 2.py", copy.Name);
            view.Delete(copy);
            Assert.DoesNotContain(view.Store.Programs, x => x.Name == "star 2.py");
            Assert.Equal(3, view.Tabs.Count());
            foreach (var x in view.Store.Programs.ToList()) view.Delete(x);
            Assert.Single(view.Store.Programs);
            Assert.Equal("main.py", view.CurrentProgram.Name);
        });
    }

    [Fact]
    public void InputRequestOpensAScratchProgram()
    {
        Headless.Run(() =>
        {
            var (_, view, _) = TestSupport.Create();
            ((IReceivesPayload)view).Receive(new InputRequest("print(6 * 7)", Evaluate: true));
            Assert.Equal("scratch.py", view.CurrentProgram.Name);
            Assert.Equal("print(6 * 7)", view.Editor.Text);
            Assert.True(TestSupport.WaitUntil(() => view.Cards.Any() && !view.Cards.First().IsRunning));
            Assert.Contains("42", view.Cards.First().ConsoleText);
        });
    }

    [Fact]
    public void FindBarHighlightsMatches()
    {
        Headless.Run(() =>
        {
            var (w, view, _) = TestSupport.Create();
            view.Editor.FocusEditor();
            w.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            Calcy.UI.TestKit.Headless.Pump();
            Assert.True(view.IsFindVisible);
            w.KeyTextInput("is_p");
            Calcy.UI.TestKit.Headless.Pump();
            Assert.Equal("is_p", view.Editor.FindText);
            Assert.Equal(6, view.Editor.FindMatchCount);
            w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal("is_p", view.Editor.SelectedText);
            w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(view.IsFindVisible);
        });
    }

    [Fact]
    public void ExamplesAllRunWithoutError()
    {
        foreach (var example in Examples.All)
        {
            var session = Calcy.Engine.CalcyEngine.CreateSession();
            using var host = new ScriptHost(session, ScriptHostOptions.Default with { Timeout = TimeSpan.FromSeconds(30) });
            var answers = new Queue<string>(example.Inputs ?? []);
            host.InputProvider = (_, _) => Task.FromResult<string?>(answers.Count > 0 ? answers.Dequeue() : "");
            var r = host.Run(example.Source, example.FileName);
            Assert.True(r.Success, example.FileName + ": " + r.Output);
            Assert.DoesNotContain("Traceback", r.Output);
        }
    }

    [Fact]
    public void ExampleSourcesUseNoDashes()
    {
        foreach (var e in Examples.All)
        {
            Assert.DoesNotContain((char)0x2013, e.Source + e.Title + e.Description);
            Assert.DoesNotContain((char)0x2014, e.Source + e.Title + e.Description);
        }
    }
}
