using System.Numerics;
using System.Text;

namespace Calcy.Python.Tests;

public class EngineApiTests
{
    private static (PythonEngine Engine, StringBuilder Output) NewEngine(PythonEngineOptions? options = null)
    {
        var e = new PythonEngine(options);
        var sb = new StringBuilder();
        e.Output = s => sb.Append(s);
        return (e, sb);
    }

    [Fact]
    public void ExecuteKeepsGlobalsUntilReset()
    {
        var (e, _) = NewEngine();
        Assert.True(e.Execute("a = 41").Success);
        Assert.True(e.Execute("a += 1").Success);
        Assert.Equal(42L, e.GetGlobal("a"));
        e.Reset();
        Assert.Null(e.GetGlobal("a"));
        var r = e.Execute("print(a)");
        Assert.Equal(ExecutionStatus.Error, r.Status);
        Assert.Equal("NameError", r.Error!.TypeName);
    }

    [Fact]
    public void InteractiveEchoesExpressionValues()
    {
        var (e, sb) = NewEngine();
        e.ExecuteInteractive("x = 5");
        var r = e.ExecuteInteractive("x * 2");
        Assert.Equal("10\n", sb.ToString());
        Assert.Equal(10L, r.Value);
        Assert.Equal("10", r.ValueRepr);
        sb.Clear();
        e.ExecuteInteractive("'hi'\nNone\n[1, 2]");
        Assert.Equal("'hi'\n[1, 2]\n", sb.ToString());
        sb.Clear();
        e.ExecuteInteractive("for i in range(3): i");
        Assert.Equal("0\n1\n2\n", sb.ToString());
        sb.Clear();
        e.ExecuteInteractive("def f():\n    return 3\n");
        Assert.Equal("", sb.ToString());
        e.ExecuteInteractive("_ + f()");
        Assert.Equal("5\n", sb.ToString());
    }

    [Fact]
    public void EvaluateReturnsConvertedValues()
    {
        var e = new PythonEngine();
        Assert.Equal(7L, e.Evaluate("3 + 4").Value);
        Assert.Equal(0.5, e.Evaluate("1 / 2").Value);
        Assert.Equal("ab", e.Evaluate("'a' + 'b'").Value);
        Assert.Equal(true, e.Evaluate("1 < 2").Value);
        Assert.Null(e.Evaluate("None").Value);
        Assert.Equal(BigInteger.Pow(2, 100), e.Evaluate("2 ** 100").Value);
        Assert.Equal(new Complex(1, 2), e.Evaluate("1 + 2j").Value);
        var list = Assert.IsAssignableFrom<IReadOnlyList<object?>>(e.Evaluate("[1, 2.5, 'x', None, (3,)]").Value);
        Assert.Equal(new object?[] { 1L, 2.5, "x", null, new List<object?> { 3L } }, list);
        var dict = Assert.IsType<Dictionary<object, object?>>(e.Evaluate("{'a': 1, 2: [3]}").Value);
        Assert.Equal(1L, dict["a"]);
        Assert.Equal("[1, 2]", e.Evaluate("[1, 2]").ValueRepr);
        var bad = e.Evaluate("x = 1");
        Assert.Equal(ExecutionStatus.Error, bad.Status);
        Assert.True(bad.Error!.IsSyntaxError);
    }

    [Fact]
    public void SetGlobalConvertsClrValues()
    {
        var e = new PythonEngine();
        e.SetGlobal("n", 5);
        e.SetGlobal("xs", new[] { 1.5, 2.5 });
        e.SetGlobal("name", "calc");
        e.SetGlobal("flag", true);
        e.SetGlobal("nothing", null);
        e.SetGlobal("big", BigInteger.Pow(10, 30));
        e.SetGlobal("map", new Dictionary<string, int> { ["k"] = 3 });
        e.SetGlobal("pair", (1, "two"));
        var r = e.Evaluate("(n + 1, sum(xs), name.upper(), flag, nothing, big + 1, map['k'], pair)");
        Assert.Equal("(6, 4.0, 'CALC', True, None, 1000000000000000000000000000001, 3, (1, 'two'))", r.ValueRepr);
        Assert.Contains("n", e.GetGlobalNames());
        Assert.DoesNotContain("__name__", e.GetGlobalNames());
        Assert.True(e.RemoveGlobal("n"));
        Assert.False(e.TryGetGlobal("n", out _));
        Assert.Throws<ArgumentException>(() => e.SetGlobal("not valid", 1));
    }

    [Fact]
    public void VariablesInspectorDoesNotRunUserCode()
    {
        var (e, sb) = NewEngine();
        e.Execute("class A:\n    def __repr__(self):\n        print('side effect')\n        return 'A'\na = A()\nx = [1, 2]");
        var vars = e.GetVariables();
        Assert.Contains(vars, v => v.Name == "x" && v.TypeName == "list" && v.Repr == "[1, 2]");
        Assert.Contains(vars, v => v.Name == "a" && v.TypeName == "A");
        Assert.DoesNotContain("side effect", sb.ToString());
    }

    [Fact]
    public void CallInvokesUserFunctions()
    {
        var e = new PythonEngine();
        e.Execute("def f(x, y=2):\n    return x ** y\n");
        Assert.Equal(9L, e.Call("f", new object?[] { 3 }).Value);
        Assert.Equal(8L, e.Call("f", new object?[] { 2, 3 }).Value);
        var missing = e.Call("nope", Array.Empty<object?>());
        Assert.Equal("NameError", missing.Error!.TypeName);
    }

    [Fact]
    public void HostModuleFunctionsAndConstants()
    {
        var (e, sb) = NewEngine();
        e.RegisterModule("calc", m =>
        {
            m.AddConstant("answer", 42);
            m.AddFunction("square", x => x * x);
            m.AddFunction("hyp", (a, b) => Math.Sqrt(a * a + b * b));
            m.AddFunction("total", args =>
            {
                args.ExpectCount(1);
                return args.GetDoubleList(0).Sum() * args.GetDouble(1, 1.0);
            });
            m.AddFunction("describe", args =>
            {
                args.ExpectKeywords("label");
                string label = args.TryGetKeyword("label", out var l) ? (string)l! : "value";
                return $"{label}={args.GetInt64(0)}";
            });
            m.AddFunction("fail", (PythonArgs _) => throw new PythonRaiseException("ValueError", "bad input"));
            m.AddFunction("crash", (PythonArgs _) => throw new InvalidOperationException("boom"));
            m.AddFunction("apply", args =>
            {
                var fn = args.GetCallable(0);
                return Enumerable.Range(0, 4).Select(i => fn.InvokeDouble(i)).ToList();
            });
            m.AddFunction("roundtrip", args => args[0]);
        });
        var r = e.Execute("""
import calc
from calc import square, answer
print(answer, square(3), calc.hyp(3, 4), calc.total([1, 2, 3]))
print(calc.describe(7), calc.describe(8, label='n'))
print(calc.apply(lambda x: x * x + 1))
print(calc.roundtrip([1, 2]) == [1, 2])
try:
    calc.fail()
except ValueError as ex:
    print('caught', ex)
try:
    calc.crash()
except RuntimeError as ex:
    print('runtime', ex)
try:
    calc.square(1, 2)
except TypeError as ex:
    print('type', ex)
""");
        Assert.True(r.Success, r.ToString());
        Assert.Equal("42 9.0 5.0 6.0\nvalue=7 n=8\n[1.0, 2.0, 5.0, 10.0]\nTrue\ncaught bad input\nruntime crash() failed: boom\ntype square() takes exactly 1 argument (2 given)\n", sb.ToString());
        Assert.Contains("calc", e.AvailableModules);
    }

    [Fact]
    public void HostObjectsAreOpaque()
    {
        var (e, sb) = NewEngine();
        var secret = new StringBuilder("secret");
        e.RegisterModule("host", m =>
        {
            m.AddFunction("make", _ => PyConvert.Wrap(secret));
            m.AddFunction("read", a => a.GetHostObject<StringBuilder>(0).ToString());
        });
        var r = e.Execute("""
import host
h = host.make()
print(type(h).__name__, host.read(h))
for name in ['Length', 'ToString', '__class__', 'GetType']:
    print(name, hasattr(h, name))
try:
    h.Append('x')
except AttributeError:
    print('no attribute access')
""");
        Assert.True(r.Success, r.ToString());
        Assert.Equal("HostObject secret\nLength False\nToString False\n__class__ True\nGetType False\nno attribute access\n", sb.ToString());
        Assert.Equal("secret", secret.ToString());
    }

    [Fact]
    public void SourceModulesAreImportable()
    {
        var (e, sb) = NewEngine();
        e.RegisterSourceModule("geometry", "import math\nPI2 = math.pi * 2\ndef area(r):\n    return math.pi * r * r\n");
        var r = e.Execute("import geometry\nfrom geometry import area\nprint(round(area(1), 4), round(geometry.PI2, 4), geometry.__name__)");
        Assert.True(r.Success, r.ToString());
        Assert.Equal("3.1416 6.2832 geometry\n", sb.ToString());
    }

    [Fact]
    public void InputCallbackAndPrompt()
    {
        var output = PyRun.Out("name = input('Name? ')\nage = int(input())\nprint(f'{name} is {age}')", inputs: new[] { "Ada", "36" });
        Assert.Equal("Name? Ada is 36\n", output);
        var eof = PyRun.Out("input('x')", inputs: Array.Empty<string>());
        Assert.Equal("x!EOFError: EOF when reading a line", eof);
    }

    [Fact]
    public void SysStdoutWriteAndPrintFile()
    {
        var (e, sb) = NewEngine();
        var err = new StringBuilder();
        e.ErrorOutput = s => err.Append(s);
        var r = e.Execute("import sys\nsys.stdout.write('a')\nprint('b', file=sys.stdout)\nprint('warn', file=sys.stderr)\nprint(1, 2, 3, sep='-', end='!\\n')");
        Assert.True(r.Success, r.ToString());
        Assert.Equal("ab\n1-2-3!\n", sb.ToString());
        Assert.Equal("warn\n", err.ToString());
    }

    [Fact]
    public void SystemExitEndsRunWithExitCode()
    {
        var (e, sb) = NewEngine();
        var r = e.Execute("print('before')\nimport sys\nsys.exit(3)\nprint('after')");
        Assert.Equal(ExecutionStatus.Exited, r.Status);
        Assert.Equal(3, r.ExitCode);
        Assert.False(r.Success);
        Assert.Equal("before\n", sb.ToString());
        var ok = e.Execute("exit()");
        Assert.True(ok.Success);
        Assert.Equal(0, ok.ExitCode);
    }

    [Fact]
    public void IsInputCompleteForRepl()
    {
        var e = new PythonEngine();
        Assert.True(e.IsInputComplete("x = 1"));
        Assert.False(e.IsInputComplete("x = (1,"));
        Assert.False(e.IsInputComplete("def f():"));
        Assert.False(e.IsInputComplete("def f():\n    return 1"));
        Assert.True(e.IsInputComplete("def f():\n    return 1\n\n"));
        Assert.False(e.IsInputComplete("s = '''abc"));
        Assert.True(e.IsInputComplete("x = = 1"));
    }

    [Fact]
    public void SleepUsesHostHook()
    {
        var (e, _) = NewEngine();
        var slept = new List<TimeSpan>();
        e.Sleep = (t, ct) =>
        {
            slept.Add(t);
            return true;
        };
        var r = e.Execute("import time\nt0 = time.monotonic()\ntime.sleep(0.25)\ntime.sleep(0)\nprint(time.time() > 1e9)");
        Assert.True(r.Success, r.ToString());
        Assert.Equal(new[] { TimeSpan.FromSeconds(0.25) }, slept);
    }

    [Fact]
    public void EngineIsNotReentrant()
    {
        var e = new PythonEngine();
        string? message = null;
        e.RegisterModule("re", m => m.AddFunction("enter", _ =>
        {
            try
            {
                e.Execute("1");
            }
            catch (InvalidOperationException ex)
            {
                message = ex.Message;
            }
            return null;
        }));
        Assert.True(e.Execute("import re\nre.enter()").Success);
        Assert.NotNull(message);
    }

    [Fact]
    public async Task AsyncApisDoNotBlockTheCaller()
    {
        var (e, sb) = NewEngine();
        var task = e.ExecuteAsync("import time\ntime.sleep(0.05)\nprint('done')");
        Assert.True(e.IsExecuting || task.IsCompleted);
        var r = await task;
        Assert.True(r.Success, r.ToString());
        Assert.False(e.IsExecuting);
        Assert.Equal("done\n", sb.ToString());
        var v = await e.EvaluateAsync("6 * 7");
        Assert.Equal(42L, v.Value);
        var i = await e.ExecuteInteractiveAsync("[x * x for x in range(4)]");
        Assert.Equal("[0, 1, 4, 9]", i.ValueRepr);
    }

    [Fact]
    public async Task ConcurrentRunsAreRejected()
    {
        var e = new PythonEngine();
        using var gate = new ManualResetEventSlim();
        e.Sleep = (t, ct) =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return true;
        };
        var task = e.ExecuteAsync("import time\ntime.sleep(1)");
        SpinWait.SpinUntil(() => e.IsExecuting, TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(() => e.Execute("1"));
        gate.Set();
        Assert.True((await task).Success);
    }

    [Fact]
    public void ResultReportsStepsAndElapsedTime()
    {
        var e = new PythonEngine();
        var r = e.Execute("s = 0\nfor i in range(1000):\n    s += i");
        Assert.True(r.Steps >= 1000);
        Assert.True(r.Elapsed > TimeSpan.Zero);
    }
}
