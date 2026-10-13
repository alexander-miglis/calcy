using System.Diagnostics;

namespace Calcy.Python.Tests;

public class LimitsTests
{
    [Fact]
    public void StepLimitStopsInfiniteLoop()
    {
        var (r, _) = PyRun.Run("while True:\n    pass", new PythonEngineOptions { MaxSteps = 100_000, Timeout = null });
        Assert.Equal(ExecutionStatus.StepLimitExceeded, r.Status);
        Assert.False(r.Success);
        Assert.InRange(r.Steps, 100_000, 102_000);
        Assert.Equal(2, r.Error!.Line);
    }

    [Fact]
    public void StepLimitCannotBeCaught()
    {
        var code = "try:\n    while True:\n        pass\nexcept BaseException:\n    print('caught')\nfinally:\n    print('finally')";
        var (r, output) = PyRun.Run(code, new PythonEngineOptions { MaxSteps = 50_000 });
        Assert.Equal(ExecutionStatus.StepLimitExceeded, r.Status);
        Assert.Equal("", output);
    }

    [Fact]
    public void TimeoutStopsLongRunningCode()
    {
        var sw = Stopwatch.StartNew();
        var (r, _) = PyRun.Run("x = 0\nwhile True:\n    x += 1", new PythonEngineOptions { Timeout = TimeSpan.FromMilliseconds(300), MaxSteps = 0 });
        Assert.Equal(ExecutionStatus.TimedOut, r.Status);
        Assert.Equal("TimeoutError", r.Error!.TypeName);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void TimeoutAppliesToNativeLoops()
    {
        var (r, _) = PyRun.Run("import itertools\nfor x in itertools.count():\n    pass", new PythonEngineOptions { Timeout = TimeSpan.FromMilliseconds(200) });
        Assert.Equal(ExecutionStatus.TimedOut, r.Status);
        var (r2, _) = PyRun.Run("print(sum(range(10 ** 15)))", new PythonEngineOptions { Timeout = TimeSpan.FromMilliseconds(200) });
        Assert.Equal(ExecutionStatus.TimedOut, r2.Status);
    }

    [Fact]
    public void CancellationRaisesUncatchableKeyboardInterrupt()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var code = "n = 0\nwhile True:\n    try:\n        n += 1\n    except Exception:\n        print('swallowed')\n    except:\n        print('bare')";
        var (r, output) = PyRun.Run(code, new PythonEngineOptions { Timeout = null, MaxSteps = 0 }, ct: cts.Token);
        Assert.Equal(ExecutionStatus.Cancelled, r.Status);
        Assert.Equal("KeyboardInterrupt", r.Error!.TypeName);
        Assert.Equal("", output);
        Assert.Contains("KeyboardInterrupt", r.Error.Traceback);
    }

    [Fact]
    public void CancellationInterruptsSleep()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var sw = Stopwatch.StartNew();
        var (r, _) = PyRun.Run("import time\ntime.sleep(30)\nprint('woke')", ct: cts.Token);
        Assert.Equal(ExecutionStatus.Cancelled, r.Status);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void AlreadyCancelledTokenStopsImmediately()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var (r, _) = PyRun.Run("for i in range(10 ** 9):\n    pass", ct: cts.Token);
        Assert.Equal(ExecutionStatus.Cancelled, r.Status);
    }

    [Fact]
    public void RecursionLimitRaisesRecursionError()
    {
        var output = PyRun.Out("def f(n):\n    return f(n + 1)\ntry:\n    f(0)\nexcept RecursionError as e:\n    print('caught', e)");
        Assert.Equal("caught maximum recursion depth exceeded\n", output);
        var (r, _) = PyRun.Run("def f(n):\n    return 1 + f(n + 1)\nf(0)");
        Assert.Equal("RecursionError", r.Error!.TypeName);
    }

    [Fact]
    public void RecursionLimitIsConfigurable()
    {
        var code = "def depth(n):\n    return n if n == 0 else 1 + depth(n - 1)\nprint(depth(400))";
        Assert.Equal("400\n", PyRun.Out(code));
        Assert.StartsWith("!RecursionError", PyRun.Out(code, new PythonEngineOptions { MaxRecursionDepth = 100 }));
        var deep = "def depth(n):\n    return n if n == 0 else 1 + depth(n - 1)\nprint(depth(20000))";
        Assert.Equal("20000\n", PyRun.Out(deep, new PythonEngineOptions { MaxRecursionDepth = 25000 }));
    }

    [Fact]
    public void DeepNativeRecursionDoesNotCrashTheHost()
    {
        var output = PyRun.Out("x = []\nfor i in range(200000):\n    x = [x]\ntry:\n    print(len(repr(x)))\nexcept RecursionError:\n    print('recursion')");
        Assert.Equal("recursion\n", output);
        var output2 = PyRun.Out("t = ()\nfor i in range(300000):\n    t = (t,)\ntry:\n    hash(t)\nexcept RecursionError:\n    print('recursion')");
        Assert.Equal("recursion\n", output2);
    }

    [Fact]
    public void MemoryErrorIsCatchable()
    {
        Assert.Equal("caught\n", PyRun.Out("try:\n    x = [1] * (10 ** 9)\nexcept MemoryError:\n    print('caught')"));
    }

    [Fact]
    public void CollectionLimitIsConfigurable()
    {
        var options = new PythonEngineOptions { MaxCollectionSize = 1000, MaxIntBits = 128 };
        Assert.Equal("500\n", PyRun.Out("print(len([0] * 500))", options));
        Assert.StartsWith("!MemoryError", PyRun.Out("x = [0] * 2000", options));
        Assert.StartsWith("!MemoryError", PyRun.Out("x = 2 ** 200", options));
        Assert.Equal("True\n", PyRun.Out("print(2 ** 100 > 0)", options));
    }
}

public class SandboxTests
{
    [Theory]
    [InlineData("import os", "ModuleNotFoundError", "No module named 'os'")]
    [InlineData("import subprocess", "ModuleNotFoundError", "No module named 'subprocess'")]
    [InlineData("import socket", "ModuleNotFoundError", "No module named 'socket'")]
    [InlineData("import ctypes", "ModuleNotFoundError", "No module named 'ctypes'")]
    [InlineData("import io", "ModuleNotFoundError", "No module named 'io'")]
    [InlineData("import builtins", "ModuleNotFoundError", "No module named 'builtins'")]
    [InlineData("import importlib", "ModuleNotFoundError", "No module named 'importlib'")]
    [InlineData("from os import path", "ModuleNotFoundError", "No module named 'os'")]
    [InlineData("__import__('os')", "ModuleNotFoundError", "No module named 'os'")]
    [InlineData("__import__('sys').modules", "AttributeError", "module 'sys' has no attribute 'modules'")]
    [InlineData("open('x.txt')", "OSError", "file system access is not available in this sandbox")]
    [InlineData("open('C:/Windows/win.ini').read()", "OSError", "file system access is not available in this sandbox")]
    [InlineData("from . import x", "ImportError", "attempted relative import with no known parent package")]
    [InlineData("compile('1', 'f', 'eval')", "NameError", "name 'compile' is not defined")]
    [InlineData("breakpoint()", "NameError", "name 'breakpoint' is not defined")]
    [InlineData("memoryview(b'')", "SyntaxError", "bytes literals are not supported in this interpreter")]
    [InlineData("import sys\nsys.modules['os']", "AttributeError", "module 'sys' has no attribute 'modules'")]
    [InlineData("().__class__.__bases__[0].__subclasses__()", "AttributeError", "type object 'object' has no attribute '__subclasses__'")]
    [InlineData("type.__subclasses__(object)", "AttributeError", "type object 'type' has no attribute '__subclasses__'")]
    [InlineData("(lambda: 0).__globals__", "AttributeError", "'function' object has no attribute '__globals__'")]
    [InlineData("(lambda: 0).__code__", "AttributeError", "'function' object has no attribute '__code__'")]
    [InlineData("def g():\n    yield\ng().gi_frame", "AttributeError", "'generator' object has no attribute 'gi_frame'")]
    [InlineData("print.__self__", "AttributeError", "'builtin_function_or_method' object has no attribute '__self__'")]
    [InlineData("import math\nmath.__loader__", "AttributeError", "module 'math' has no attribute '__loader__'")]
    [InlineData("__builtins__", "NameError", "name '__builtins__' is not defined")]
    [InlineData("object.__reduce_ex__", "AttributeError", "type object 'object' has no attribute '__reduce_ex__'")]
    [InlineData("exec('import os')", "ModuleNotFoundError", "No module named 'os'")]
    [InlineData("eval(\"__import__('subprocess')\")", "ModuleNotFoundError", "No module named 'subprocess'")]
    [InlineData("getattr(__import__('math'), 'sys')", "AttributeError", "module 'math' has no attribute 'sys'")]
    [InlineData("import time\ntime.os", "AttributeError", "module 'time' has no attribute 'os'")]
    public void DangerousOperationsAreUnavailable(string code, string errorType, string message)
    {
        var (r, _) = PyRun.Run(code);
        Assert.Equal(ExecutionStatus.Error, r.Status);
        Assert.Equal(errorType, r.Error!.TypeName);
        Assert.Equal(message, r.Error.Message);
    }

    [Fact]
    public void IntrospectionOnlyReachesInterpreterObjects()
    {
        var output = PyRun.Out("""
mro = ().__class__.__mro__
print([c.__name__ for c in mro])
base = mro[-1]
print(base.__name__, [n for n in dir(base) if n in ('__subclasses__', '__globals__', '__code__')])
print(type(type).__name__, type.__class__.__name__)
f = lambda: 1
print([n for n in dir(f) if n in ('__globals__', '__code__', '__closure__', '__builtins__')])
print(type(print).__name__, hasattr(print, '__module__'))
""");
        Assert.Equal("['tuple', 'object']\nobject []\ntype type\n[]\nbuiltin_function_or_method True\n", output);
    }

    [Fact]
    public void HostModuleCannotBeReplacedByPythonCode()
    {
        var e = new PythonEngine();
        e.RegisterModule("calc", m => m.AddFunction("f", (PythonArgs _) => 1));
        var r = e.Execute("import calc\ncalc.f = lambda: 'patched'\nimport calc as c2\nprint(c2.f())");
        Assert.True(r.Success, r.ToString());
        e.Reset();
        var output = new System.Text.StringBuilder();
        e.Output = s => output.Append(s);
        e.Execute("import calc\nprint(calc.f())");
        Assert.Equal("1\n", output.ToString());
    }

    [Fact]
    public void EvalAndExecRespectLimits()
    {
        var (r, _) = PyRun.Run("exec('while True: pass')", new PythonEngineOptions { MaxSteps = 10_000 });
        Assert.Equal(ExecutionStatus.StepLimitExceeded, r.Status);
        var (r2, _) = PyRun.Run("eval('[0] * 10 ** 9')");
        Assert.Equal("MemoryError", r2.Error!.TypeName);
    }
}

/// <summary>Tests that measure time or process memory run without other tests in parallel.</summary>
[CollectionDefinition("Serial", DisableParallelization = true)]
public class SerialCollection
{
}

[Collection("Serial")]
public class MemoryGuardTests
{
    [Theory]
    [InlineData("x = [0] * (10 ** 8)")]
    [InlineData("x = 'a' * (10 ** 8)")]
    [InlineData("x = []\nwhile True:\n    x.append(1)")]
    [InlineData("x = list(range(10 ** 8))")]
    [InlineData("d = {}\ni = 0\nwhile True:\n    d[i] = i\n    i += 1")]
    [InlineData("s = set(range(10 ** 8))")]
    [InlineData("x = 'ab'\nwhile True:\n    x = x + x")]
    [InlineData("x = 10 ** (10 ** 7)")]
    [InlineData("x = 2 ** 2 ** 25")]
    [InlineData("x = 1 << (10 ** 8)")]
    [InlineData("x = 7\nwhile True:\n    x = x * x")]
    [InlineData("import math\nmath.factorial(10 ** 6)")]
    [InlineData("'{:>200000000}'.format(1)")]
    public void MemoryCapsRaiseMemoryError(string code)
    {
        var (r, _) = PyRun.Run(code, new PythonEngineOptions { Timeout = TimeSpan.FromSeconds(30) });
        Assert.Equal(ExecutionStatus.Error, r.Status);
        Assert.Equal("MemoryError", r.Error!.TypeName);
    }

    [Fact]
    public void TotalMemoryGuardRaisesMemoryError()
    {
        var options = new PythonEngineOptions { MaxMemoryBytes = 64L * 1024 * 1024, Timeout = TimeSpan.FromSeconds(30) };
        var (r, _) = PyRun.Run("rows = [[0] * 1000 for _ in range(100000)]", options);
        Assert.Equal(ExecutionStatus.Error, r.Status);
        Assert.Equal("MemoryError", r.Error!.TypeName);
    }
}

[Collection("Serial")]
public class PerformanceTests
{
    [Fact]
    public void TightLoopRunsFast()
    {
        var e = new PythonEngine(new PythonEngineOptions { Timeout = TimeSpan.FromSeconds(60) });
        e.Execute("s = 0\nfor i in range(1000):\n    s += i * i");
        var sw = Stopwatch.StartNew();
        var r = e.Execute("s = 0\nfor i in range(1000000):\n    s += i * i");
        sw.Stop();
        Assert.True(r.Success, r.ToString());
        Assert.Equal(333332833333500000L, e.GetGlobal("s"));
        // about 0.1 to 0.3 s in Release; generous bound for Debug builds and slow CI machines
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }

    [Fact]
    public void FunctionCallsAndRecursionAreReasonablyFast()
    {
        var e = new PythonEngine();
        var sw = Stopwatch.StartNew();
        var r = e.Execute("def fib(n):\n    return n if n < 2 else fib(n - 1) + fib(n - 2)\nresult = fib(24)");
        sw.Stop();
        Assert.True(r.Success, r.ToString());
        Assert.Equal(46368L, e.GetGlobal("result"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
    }

    [Fact]
    public void ListAndDictWorkloads()
    {
        var e = new PythonEngine();
        var sw = Stopwatch.StartNew();
        var r = e.Execute("""
squares = [i * i for i in range(200000)]
counts = {}
for v in squares:
    k = v % 97
    counts[k] = counts.get(k, 0) + 1
words = ' '.join(str(i) for i in range(50000)).split()
total = sum(len(w) for w in words)
ordered = sorted(squares, reverse=True)[:3]
""");
        sw.Stop();
        Assert.True(r.Success, r.ToString());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
    }
}
