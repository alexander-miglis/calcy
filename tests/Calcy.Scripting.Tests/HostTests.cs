using System.Diagnostics;
using Calcy.Python;

namespace Calcy.Scripting.Tests;

public class HostTests
{
    [Fact]
    public void InputComesFromTheProvider()
    {
        using var t = new TestHost();
        var prompts = new List<string>();
        var answers = new Queue<string>(["Ada", "36"]);
        t.Host.InputProvider = (req, ct) =>
        {
            prompts.Add(req.Prompt);
            return Task.FromResult<string?>(answers.Dequeue());
        };
        var r = t.Run("name = input('Name? ')\nage = int(input('Age? '))\nprint(name, 'is', age)");
        Assert.True(r.Success, r.Output);
        Assert.Equal(["Name? ", "Age? "], prompts);
        Assert.Equal("Name? Ada\nAge? 36\nAda is 36\n", t.Output);
        Assert.Equal(t.Output, r.Output);
    }

    [Fact]
    public void InputWithoutProviderRaisesEofError()
    {
        using var t = new TestHost();
        var r = t.Run("input('x')");
        Assert.Equal(ExecutionStatus.Error, r.Status);
        Assert.Equal("EOFError", r.Error!.TypeName);
    }

    [Fact]
    public async Task InputWaitsAndStopEndsIt()
    {
        using var t = new TestHost();
        var gate = new TaskCompletionSource<string?>();
        t.Host.InputProvider = (_, ct) =>
        {
            ct.Register(() => gate.TrySetCanceled());
            return gate.Task;
        };
        var run = t.Host.RunAsync("input('wait ')\nprint('never')");
        await WaitUntil(() => t.Output.Contains("wait "));
        Assert.True(t.Host.IsRunning);
        var sw = Stopwatch.StartNew();
        t.Host.Stop();
        var r = await run;
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), sw.Elapsed.ToString());
        Assert.Equal(ExecutionStatus.Cancelled, r.Status);
        Assert.DoesNotContain("never", r.Output);
    }

    [Fact]
    public void TimeoutEndsRunawayPrograms()
    {
        using var t = new TestHost(ScriptHostOptions.Default with { Timeout = TimeSpan.FromMilliseconds(300) });
        var r = t.Run("while True:\n    pass");
        Assert.Equal(ExecutionStatus.TimedOut, r.Status);
        Assert.Equal("Time limit reached", r.Summary);
        Assert.InRange(r.ErrorLine ?? 0, 1, 2);
    }

    [Fact]
    public async Task StopEndsAnInfiniteLoopQuickly()
    {
        using var t = new TestHost();
        var run = t.Host.RunAsync("n = 0\nwhile True:\n    n += 1");
        await WaitUntil(() => t.Host.IsRunning);
        await Task.Delay(100);
        var sw = Stopwatch.StartNew();
        t.Host.Stop();
        var r = await run;
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), sw.Elapsed.ToString());
        Assert.Equal(ExecutionStatus.Cancelled, r.Status);
        Assert.Equal("KeyboardInterrupt", r.Error!.TypeName);
        Assert.False(t.Host.IsRunning);
    }

    [Fact]
    public async Task WaitingForFramesDoesNotUseTheTimeLimit()
    {
        // A wait longer than the limit; the margin keeps the test steady on busy machines.
        using var t = new TestHost(ScriptHostOptions.Default with { Timeout = TimeSpan.FromMilliseconds(1500) });
        var r = await t.Host.RunAsync("import calcydraw as d\nd.wait(1.8)\nprint('done')");
        Assert.True(r.Success, r.Output);
        using var strict = new TestHost(ScriptHostOptions.Default with { Timeout = TimeSpan.FromMilliseconds(1500), PauseTimeoutWhileWaiting = false });
        var r2 = await strict.Host.RunAsync("import calcydraw as d\nd.wait(1.8)\nprint('done')");
        Assert.Equal(ExecutionStatus.TimedOut, r2.Status);
    }

    [Fact]
    public async Task KeysReachGetKey()
    {
        using var t = new TestHost();
        var run = t.Host.RunAsync("""
import ti_system
print(ti_system.get_key())
k = ti_system.wait_key()
print(k, ti_system.is_pressed("Up"))
""");
        await WaitUntil(() => t.Output.Length > 0);
        t.Host.Keyboard.Press("Up");
        var r = await run;
        Assert.True(r.Success, r.Output);
        Assert.Equal("\nup True\n", t.Output);
        Assert.Equal("enter", ScriptKeyboard.Normalize("Return"));
        Assert.Equal("7", ScriptKeyboard.Normalize("NumPad7"));
        Assert.Equal("space", ScriptKeyboard.Normalize(" "));
    }

    [Fact]
    public void TiSystemSharesCalculatorVariables()
    {
        using var t = new TestHost();
        t.Session.Evaluate("f(x) = 3x");
        var output = t.Out("""
from ti_system import *
store_value("score", 42)
store_list("L1", [1, 2, 3])
print(recall_value("score"), recall_list("L1"), eval_function("f", 2))
disp_at(1, "hello")
""");
        Assert.Equal("42 [1, 2, 3] 6\nhello\n", output);
        Assert.Equal("42", t.Session.Format(t.Session.Evaluate("score")).Primary);
    }

    [Theory]
    [InlineData("open('secret.txt')", "OSError")]
    [InlineData("import os", "ModuleNotFoundError")]
    [InlineData("import subprocess", "ModuleNotFoundError")]
    [InlineData("import socket", "ModuleNotFoundError")]
    [InlineData("import urllib", "ModuleNotFoundError")]
    [InlineData("import ctypes", "ModuleNotFoundError")]
    [InlineData("import numpy", "ModuleNotFoundError")]
    [InlineData("__import__('io')", "ModuleNotFoundError")]
    public void SandboxBlocksFilesNetworkAndSystem(string code, string error)
    {
        using var t = new TestHost();
        var r = t.Run(code);
        Assert.Equal(ExecutionStatus.Error, r.Status);
        Assert.Equal(error, r.Error!.TypeName);
    }

    [Fact]
    public void HostObjectsStayOpaque()
    {
        using var t = new TestHost();
        var output = t.Out("""
import _calcy
print([n for n in dir(_calcy) if n.startswith('__') and n not in ('__name__', '__doc__')])
print(type(_calcy.canvas_size).__name__)
""");
        Assert.Equal("[]\nbuiltin_function_or_method\n", output);
        Assert.DoesNotContain("_calcy", t.Host.AvailableModules);
        Assert.Contains("turtle", t.Host.AvailableModules);
        Assert.Contains("math", t.Host.AvailableModules);
    }

    [Fact]
    public void ErrorLinePointsIntoTheProgram()
    {
        using var t = new TestHost();
        var r = t.Run("import calc\n\ndef f(x):\n    return calc.eval('1/' + str(x))\n\nf(0)", "prog.py");
        Assert.Equal(ExecutionStatus.Error, r.Status);
        Assert.Equal(4, r.ErrorLine);
        Assert.Equal("ValueError", r.Error!.TypeName);
        Assert.Contains("Traceback", r.Output);
        var syntax = t.Run("x = (1,\nprint(x", "prog.py");
        Assert.True(syntax.Error!.IsSyntaxError);
        Assert.NotNull(syntax.ErrorLine);
    }

    [Fact]
    public void ReplKeepsTheProgramsGlobals()
    {
        using var t = new TestHost();
        t.Out("import calc\nx = 5");
        var r = t.Host.ExecuteInteractive("x * 2");
        Assert.True(r.Success, r.Output);
        Assert.Equal("10", r.ValueRepr);
        Assert.Null(r.MathValue);
        var math = t.Host.ExecuteInteractive("calc.eval('sqrt(8)')");
        Assert.Equal("2√2", math.ValueRepr);
        Assert.Equal("2*sqrt(2)", math.MathValue);
        var frac = t.Host.ExecuteInteractive("calc.eval('1/3')");
        Assert.Equal("(1/3)", frac.MathValue);
        Assert.True(t.Host.IsInputComplete("x = 1"));
        Assert.False(t.Host.IsInputComplete("for i in range(3):"));
        Assert.Contains(t.Host.GetVariables(), v => v.Name == "x" && v.Repr == "5");
    }

    [Fact]
    public void OutputIsCapped()
    {
        using var t = new TestHost(ScriptHostOptions.Default with { MaxOutputChars = 5000 });
        var r = t.Run("for i in range(100000):\n    print(i)");
        Assert.True(r.Success);
        Assert.True(r.OutputTruncated);
        Assert.True(r.Output.Length < 6000);
        Assert.Contains("output stopped", r.Output);
    }

    [Fact]
    public void HelpShowsDocstringsAndReferenceListsThem()
    {
        using var t = new TestHost();
        var output = t.Out("import turtle\nhelp(turtle.forward)\nimport calc\nhelp(calc.solve)");
        Assert.StartsWith("forward(distance): move forward", output);
        Assert.Contains("solve(equation, variable=\"x\")", output);
        var docs = ScriptHost.Reference;
        Assert.Contains(docs, d => d.Module == "turtle" && d.Name == "forward" && d.Signature == "forward(distance)");
        Assert.Contains(docs, d => d.Module == "turtle" && d.Name == "Turtle.circle");
        Assert.Contains(docs, d => d.Module == "calc" && d.Name == "eval");
        Assert.Contains(docs, d => d.Module == "ti_plotlib" && d.Name == "scatter");
        Assert.Contains(docs, d => d.Module == "casioplot" && d.Name == "set_pixel");
        Assert.Contains(docs, d => d.Module == "ti_draw" && d.Name == "fill_circle");
        Assert.Contains(docs, d => d.Module == "calcydraw" && d.Name == "run");
        Assert.Contains(docs, d => d.Module == "ti_system" && d.Name == "get_key");
        foreach (var d in docs) Assert.False(string.IsNullOrWhiteSpace(d.Summary), d.Module + "." + d.Name);
    }

    [Fact]
    public void ConsoleClearIsReported()
    {
        using var t = new TestHost();
        var kinds = new List<ScriptOutputKind>();
        t.Host.Output += (_, e) => kinds.Add(e.Kind);
        t.Out("import ti_system\nprint('a')\nti_system.disp_clr()\nprint('b', file=__import__('sys').stderr)");
        Assert.Equal([ScriptOutputKind.Standard, ScriptOutputKind.Clear, ScriptOutputKind.Error], kinds);
    }

    [Fact]
    public void EachRunStartsFresh()
    {
        using var t = new TestHost();
        t.Out("import turtle\nturtle.forward(10)\ny = 1");
        Assert.True(t.Host.Canvas.HasDrawing);
        var r = t.Run("print('y' in globals())");
        Assert.True(r.Success);
        Assert.Equal("False\n", t.Output);
        Assert.False(t.Host.Canvas.HasDrawing);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("condition not met");
            await Task.Delay(10);
        }
    }
}
