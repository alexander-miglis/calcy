using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace Calcy.Python.Tests;

public class TimeLimitPauseTests
{
    private static PythonEngine NewEngine(TimeSpan? timeout, StringBuilder sb)
    {
        var e = new PythonEngine(new PythonEngineOptions { Timeout = timeout });
        e.Output = s => sb.Append(s);
        e.RegisterModule("host", m =>
        {
            m.AddFunction("wait_paused", args =>
            {
                using (args.PauseTimeLimit()) Thread.Sleep((int)args.GetInt64(0));
                return null;
            });
            m.AddFunction("wait_engine", args => e.WaitOutsideTimeLimit(ct =>
            {
                ct.WaitHandle.WaitOne((int)args.GetInt64(0));
                return "waited";
            }));
            m.AddFunction("wait_plain", args =>
            {
                Thread.Sleep((int)args.GetInt64(0));
                return null;
            });
            m.AddFunction("wait_nested", args =>
            {
                using (args.PauseTimeLimit())
                {
                    var inner = e.PauseTimeLimit();
                    inner.Dispose();
                    inner.Dispose();
                    Thread.Sleep((int)args.GetInt64(0));
                }
                return null;
            });
            m.AddFunction("token_matches", args => args.CancellationToken == e.CancellationToken);
        });
        return e;
    }

    [Fact]
    public void PausedWaitsDoNotCountAgainstTheTimeout()
    {
        var sb = new StringBuilder();
        var e = NewEngine(TimeSpan.FromSeconds(1), sb);
        // 1.2 s of paused waits against a 1 s limit; the margin keeps the test steady on busy machines.
        var r = e.Execute("import host\nfor i in range(3):\n    host.wait_paused(400)\ns = 0\nfor i in range(5000):\n    s += i\nprint('done', s)");
        Assert.True(r.Success, r.ToString());
        Assert.Equal("done 12497500\n", sb.ToString());
        Assert.True(r.Elapsed >= TimeSpan.FromMilliseconds(1200));
    }

    [Fact]
    public void UnpausedWaitsCountAgainstTheTimeout()
    {
        var sb = new StringBuilder();
        var e = NewEngine(TimeSpan.FromMilliseconds(300), sb);
        var r = e.Execute("import host\nhost.wait_plain(600)\nfor i in range(20000):\n    pass\nprint('not reached')");
        Assert.Equal(ExecutionStatus.TimedOut, r.Status);
        Assert.Equal("", sb.ToString());
    }

    [Fact]
    public void EngineWaitOutsideTimeLimitReturnsTheValue()
    {
        var sb = new StringBuilder();
        var e = NewEngine(TimeSpan.FromSeconds(1), sb);
        var r = e.Execute("import host\nprint(host.wait_engine(1200), host.token_matches())\nfor i in range(5000):\n    pass");
        Assert.True(r.Success, r.ToString());
        Assert.Equal("waited True\n", sb.ToString());
    }

    [Fact]
    public void NestedPausesKeepTheOuterPause()
    {
        var sb = new StringBuilder();
        var e = NewEngine(TimeSpan.FromSeconds(1), sb);
        var r = e.Execute("import host\nhost.wait_nested(1200)\nfor i in range(5000):\n    pass\nprint('ok')");
        Assert.True(r.Success, r.ToString());
        Assert.Equal("ok\n", sb.ToString());
    }

    [Fact]
    public async Task CancellationStopsAWaitingHostFunction()
    {
        var sb = new StringBuilder();
        var e = NewEngine(null, sb);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var sw = Stopwatch.StartNew();
        var r = await e.ExecuteAsync("import host\nhost.wait_engine(10000)\nprint('after')", "<main>", cts.Token);
        Assert.Equal(ExecutionStatus.Cancelled, r.Status);
        Assert.Equal("KeyboardInterrupt", r.Error!.TypeName);
        Assert.Equal("", sb.ToString());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }

    [Fact]
    public void EnginePauseRequiresTheInterpreterThread()
    {
        var e = new PythonEngine();
        Assert.Throws<InvalidOperationException>(() => e.PauseTimeLimit());
        Assert.Throws<InvalidOperationException>(() => e.WaitOutsideTimeLimit(_ => 1));
        Assert.Equal(CancellationToken.None, e.CancellationToken);
    }
}

public class HostCallableTests
{
    private static (PythonEngine Engine, StringBuilder Output) NewEngine(PythonEngineOptions? options = null)
    {
        var e = new PythonEngine(options);
        var sb = new StringBuilder();
        e.Output = s => sb.Append(s);
        return (e, sb);
    }

    [Fact]
    public void CallsRunOnTheCallingThread()
    {
        var (e, _) = NewEngine();
        int? seen = null;
        e.RegisterModule("host", m => m.AddFunction("where", (PythonArgs _) =>
        {
            seen = Environment.CurrentManagedThreadId;
            return null;
        }));
        Assert.True(e.Execute("import host\ndef f(x):\n    host.where()\n    return x + 1").Success);
        var f = e.GetCallable("f");
        Assert.Equal(42L, f.Invoke(41));
        Assert.Equal(Environment.CurrentManagedThreadId, seen);
        Assert.False(e.IsExecuting);
    }

    [Fact]
    public void ConvertsArgumentsAndResults()
    {
        var (e, sb) = NewEngine();
        e.Execute("def pack(*a, **k):\n    return list(a)\ndef hyp(x, y):\n    return (x * x + y * y) ** 0.5\ndef shout(s):\n    print(s.upper())\n    return len(s)");
        var list = Assert.IsType<List<object?>>(e.GetCallable("pack").Invoke(1, "a", 2.5, null));
        Assert.Equal(new object?[] { 1L, "a", 2.5, null }, list);
        Assert.Equal(5.0, e.GetCallable("hyp").InvokeDouble(3, 4));
        Assert.True(e.GetCallable("hyp").TryInvokeDouble(6, 8, out var h));
        Assert.Equal(10.0, h);
        Assert.Equal(2L, e.GetCallable("shout").Invoke("hi"));
        Assert.Equal("HI\n", sb.ToString());
    }

    [Fact]
    public void FindsGlobalsBuiltinsClassesAndCallableObjects()
    {
        var (e, _) = NewEngine();
        e.Execute("class P:\n    def __init__(self, x):\n        self.x = x\n    def __call__(self, y):\n        return self.x + y\np = P(5)\nn = 5");
        Assert.Equal(3L, e.GetCallable("abs").Invoke(-3));
        Assert.Equal("P", e.GetCallable("P").InvokeRaw(1).TypeName);
        Assert.Equal(7.0, e.GetCallable("p").InvokeDouble(2));
        Assert.Equal(6.0, e.AsCallable(e.GetGlobalObject("p")!).InvokeDouble(1));
        Assert.Throws<KeyNotFoundException>(() => e.GetCallable("missing"));
        Assert.Throws<ArgumentException>(() => e.GetCallable("n"));
        Assert.False(e.TryGetCallable("n", out _));
        Assert.False(e.TryGetCallable("missing", out _));
        Assert.True(e.TryGetCallable("p", out var pc));
        Assert.Equal(15.0, pc.InvokeDouble(10));
    }

    [Fact]
    public void SeesTheCurrentGlobals()
    {
        var (e, _) = NewEngine();
        PythonCallable? kept = null;
        e.RegisterModule("host", m => m.AddFunction("keep", args =>
        {
            kept = args.GetCallable(0);
            return null;
        }));
        Assert.True(e.Execute("import host\nfactor = 3\nhost.keep(lambda x: factor * x)").Success);
        Assert.Equal(6.0, kept!.InvokeDouble(2));
        e.SetGlobal("factor", 10);
        Assert.Equal(20.0, kept.InvokeDouble(2));
        Assert.Same(e, kept.Engine);
    }

    [Fact]
    public void ErrorsThrowOrReturnFalse()
    {
        var (e, _) = NewEngine();
        e.Execute("def inv(x):\n    return 1 / x\ndef text(x):\n    return 'text'\ndef bye(x):\n    raise SystemExit(3)");
        var inv = e.GetCallable("inv");
        Assert.True(inv.TryInvokeDouble(4, out var y));
        Assert.Equal(0.25, y);
        Assert.False(inv.TryInvokeDouble(0, out y));
        Assert.True(double.IsNaN(y));
        var ex = Assert.Throws<PythonCallException>(() => inv.InvokeDouble(0));
        Assert.Equal(ExecutionStatus.Error, ex.Status);
        Assert.Equal("ZeroDivisionError", ex.Error.TypeName);
        Assert.Equal("float division by zero", ex.Error.Message);
        Assert.Contains("return 1 / x", ex.Error.Traceback);
        Assert.Equal(2, ex.Error.Line);
        Assert.False(e.GetCallable("text").TryInvokeDouble(1, out _));
        var exit = Assert.Throws<PythonCallException>(() => e.GetCallable("bye").TryInvokeDouble(1, out _));
        Assert.Equal(ExecutionStatus.Exited, exit.Status);
        Assert.True(e.Execute("print('still fine')").Success);
    }

    [Fact]
    public void LimitsApplyPerCall()
    {
        var (e, _) = NewEngine();
        e.Execute("def spin(x):\n    while True:\n        x += 1");
        var spin = e.GetCallable("spin", new PythonCallOptions { MaxSteps = 10_000 });
        var ex = Assert.Throws<PythonCallException>(() => spin.InvokeDouble(0));
        Assert.Equal(ExecutionStatus.StepLimitExceeded, ex.Status);
        Assert.Throws<PythonCallException>(() => spin.TryInvokeDouble(0, out _));

        var timed = spin.WithOptions(new PythonCallOptions { MaxSteps = 0, Timeout = TimeSpan.FromMilliseconds(100) });
        var sw = Stopwatch.StartNew();
        ex = Assert.Throws<PythonCallException>(() => timed.InvokeDouble(0));
        Assert.Equal(ExecutionStatus.TimedOut, ex.Status);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var cancellable = spin.WithOptions(new PythonCallOptions { MaxSteps = 0, Timeout = null, CancellationToken = cts.Token });
        ex = Assert.Throws<PythonCallException>(() => cancellable.InvokeDouble(0));
        Assert.Equal(ExecutionStatus.Cancelled, ex.Status);
        Assert.Equal("KeyboardInterrupt", ex.Error.TypeName);
        ex = Assert.Throws<PythonCallException>(() => e.GetCallable("abs", new PythonCallOptions { CancellationToken = cts.Token }).Invoke(1));
        Assert.Equal(ExecutionStatus.Cancelled, ex.Status);

        // a fresh call starts with fresh counters
        e.Execute("def short(x):\n    for i in range(100):\n        x += 1\n    return x");
        var shortCall = e.GetCallable("short", new PythonCallOptions { MaxSteps = 1000 });
        for (int i = 0; i < 50; i++) Assert.Equal(100.0 + i, shortCall.InvokeDouble(i));
    }

    [Fact]
    public void RecursionDepthIsBounded()
    {
        var (e, _) = NewEngine();
        e.Execute("def down(n):\n    return down(n + 1)\ndef depth(n):\n    try:\n        return depth(n + 1)\n    except RecursionError:\n        return n");
        var ex = Assert.Throws<PythonCallException>(() => e.GetCallable("down").Invoke(0));
        Assert.Equal("RecursionError", ex.Error.TypeName);
        var reached = (long)e.GetCallable("depth").Invoke(0)!;
        Assert.InRange(reached, 190, 200);
        var deeper = (long)e.GetCallable("depth", new PythonCallOptions { MaxRecursionDepth = 500 }).Invoke(0)!;
        Assert.InRange(deeper, 490, 500);
    }

    [Fact]
    public void DeepRecursionOnASmallStackRaisesInsteadOfCrashing()
    {
        object? depth = null, sum = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var e = new PythonEngine(new PythonEngineOptions { MaxRecursionDepth = 1_000_000 });
                e.Execute("def depth(n):\n    try:\n        return depth(n + 1)\n    except RecursionError:\n        return n\ndef deep(n):\n    return eval('+'.join(['1'] * n))");
                var unlimited = new PythonCallOptions { MaxRecursionDepth = 1_000_000, MaxSteps = 0, Timeout = TimeSpan.FromSeconds(30) };
                depth = e.GetCallable("depth", unlimited).Invoke(0);
                try
                {
                    sum = e.GetCallable("deep", unlimited).Invoke(100_000);
                }
                catch (PythonCallException pe) when (pe.Error.TypeName == "RecursionError")
                {
                    sum = "RecursionError";
                }
            }
            catch (Exception x)
            {
                failure = x;
            }
        }, 1024 * 1024);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
        Assert.True((long)depth! > 50, $"depth {depth}");
        Assert.True(sum is 100_000L or "RecursionError", $"sum {sum}");
    }

    [Fact]
    public void NestedCallsInsideARunShareItAndPropagateErrors()
    {
        var (e, sb) = NewEngine();
        e.RegisterModule("host", m => m.AddFunction("call_it", args => args.GetCallable(0).Invoke()));
        var r = e.Execute("import host\ntry:\n    host.call_it(lambda: 1 / 0)\nexcept ZeroDivisionError as ex:\n    print('caught', ex)\nprint(host.call_it(lambda: 'ok'))");
        Assert.True(r.Success, r.ToString());
        Assert.Equal("caught division by zero\nok\n", sb.ToString());
    }

    [Fact]
    public void CallsFromHostFunctionsOfDirectCallsNest()
    {
        var (e, _) = NewEngine();
        PythonCallable? inner = null;
        e.RegisterModule("host", m => m.AddFunction("call_inner", args => inner!.Invoke(args[0])));
        e.Execute("import host\ndef twice(x):\n    return 2 * x\ndef outer(x):\n    return host.call_inner(x) + 1");
        inner = e.GetCallable("twice");
        Assert.Equal(11L, e.GetCallable("outer").Invoke(5));
    }

    [Fact]
    public void EnginesCanCallEachOther()
    {
        var a = new PythonEngine();
        var b = new PythonEngine();
        PythonCallable? aSquare = null;
        b.RegisterModule("bridge", m => m.AddFunction("square_in_a", args => aSquare!.Invoke(args[0])));
        b.Execute("import bridge\ndef plus_square(x):\n    return x + bridge.square_in_a(x)");
        var bPlus = b.GetCallable("plus_square");
        a.RegisterModule("bridge", m => m.AddFunction("in_b", args => bPlus.Invoke(args[0])));
        a.Execute("import bridge\ndef square(x):\n    return x * x\ndef go(x):\n    return bridge.in_b(x)");
        aSquare = a.GetCallable("square");
        Assert.Equal(12L, a.GetCallable("go").Invoke(3));
    }

    [Fact]
    public void GeneratorsCanBeDrivenByLaterCalls()
    {
        var (e, _) = NewEngine();
        e.Execute("def counter():\n    n = 0\n    while True:\n        n += 1\n        yield n");
        var gen = e.GetCallable("counter").InvokeRaw();
        var next = e.GetCallable("next");
        Assert.Equal(1L, next.Invoke(gen));
        Assert.Equal(2L, next.Invoke(gen));
    }

    [Fact]
    public async Task CallsAreRejectedWhileTheEngineRunsElsewhere()
    {
        var (e, _) = NewEngine();
        using var gate = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        e.RegisterModule("host", m => m.AddFunction("block", args =>
        {
            entered.Set();
            return args.WaitOutsideTimeLimit(ct => gate.Wait(5000, ct));
        }));
        e.Execute("import host\ndef f(x):\n    return x");
        var f = e.GetCallable("f");
        var run = e.ExecuteAsync("host.block()");
        Assert.True(entered.Wait(5000));
        Assert.Throws<InvalidOperationException>(() => f.InvokeDouble(1));
        gate.Set();
        Assert.True((await run).Success);
        Assert.Equal(1.0, f.InvokeDouble(1));
    }

    [Fact]
    public void PausesWorkInDirectCalls()
    {
        var (e, _) = NewEngine();
        e.RegisterModule("host", m =>
        {
            m.AddFunction("slow", args => args.WaitOutsideTimeLimit(_ =>
            {
                Thread.Sleep(1200);
                return 1;
            }));
            m.AddFunction("slow_plain", (PythonArgs _) =>
            {
                Thread.Sleep(1200);
                return 1;
            });
        });
        // The loops are long enough to reach the interpreter's periodic time check.
        e.Execute("import host\ndef paused(x):\n    x += host.slow()\n    for i in range(5000):\n        x += 1\n    return x\ndef plain(x):\n    x += host.slow_plain()\n    for i in range(5000):\n        x += 1\n    return x");
        var options = new PythonCallOptions { Timeout = TimeSpan.FromSeconds(1) };
        Assert.Equal(5001.0, e.GetCallable("paused", options).InvokeDouble(0));
        var ex = Assert.Throws<PythonCallException>(() => e.GetCallable("plain", options).InvokeDouble(0));
        Assert.Equal(ExecutionStatus.TimedOut, ex.Status);
    }

    [Fact]
    public void StrSubclassValuesReachTheHostAsStrings()
    {
        var (e, sb) = NewEngine();
        e.RegisterModule("host", m => m.AddFunction("text", args => args.GetString(0) + "|" + PyConvert.ToClr(args[0])));
        var r = e.Execute("import host\nclass Expr(str):\n    def __new__(cls, text, pretty=None):\n        self = super().__new__(cls, text)\n        self.pretty = pretty or text\n        return self\n    def __repr__(self):\n        return self.pretty\nex = Expr('x^2', 'x**2')\nprint(host.text(ex), repr(ex), isinstance(ex, str), ex + ' = 0')");
        Assert.True(r.Success, r.ToString());
        Assert.Equal("x^2|x^2 x**2 True x^2 = 0\n", sb.ToString());
        Assert.Equal("x^2", e.GetGlobal("ex"));
        Assert.Equal("Expr", e.GetGlobalObject("ex")!.TypeName);
        var v = e.Evaluate("ex");
        Assert.Equal("x**2", v.ValueRepr);
        Assert.Equal("x^2", v.Value);
    }
}

[Collection("Serial")]
public class HostCallablePerformanceTests
{
    private readonly ITestOutputHelper _output;

    public HostCallablePerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TenThousandLambdaCallsAreFast()
    {
        var e = new PythonEngine();
        e.Execute("f = lambda x: x * x + 1\ng = lambda x: 1 / x");
        var f = e.GetCallable("f");
        double warm = 0;
        for (int i = 0; i < 2000; i++) warm += f.InvokeDouble(i);
        var sw = Stopwatch.StartNew();
        double sum = 0;
        for (int i = 0; i < 10_000; i++) sum += f.InvokeDouble(i);
        sw.Stop();
        Assert.Equal(Enumerable.Range(0, 10_000).Sum(i => (double)i * i + 1), sum);
        _output.WriteLine($"10,000 InvokeDouble calls: {sw.Elapsed.TotalMilliseconds:0.00} ms");
        // a few ms in Release; generous bound for Debug builds and slow CI machines
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(500), $"took {sw.Elapsed}");

        var g = e.GetCallable("g");
        sw.Restart();
        int failed = 0;
        for (int i = 0; i < 10_000; i++)
            if (!g.TryInvokeDouble(i % 2, out _)) failed++;
        sw.Stop();
        Assert.Equal(5000, failed);
        _output.WriteLine($"10,000 TryInvokeDouble calls, half raising: {sw.Elapsed.TotalMilliseconds:0.00} ms");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }
}
